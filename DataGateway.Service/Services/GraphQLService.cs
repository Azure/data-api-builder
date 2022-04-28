using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.GraphQLBuilder.Sql;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataGateway.Service.Services
{
    public class GraphQLService
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly IGraphQLMetadataProvider _graphQLMetadataProvider;
        private readonly DataGatewayConfig _config;
        private readonly IRuntimeConfigProvider _runtimeConfigProvider;
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly IDocumentCache _documentCache;
        private readonly IDocumentHashProvider _documentHashProvider;

        public ISchema? Schema { private set; get; }
        public IRequestExecutor? Executor { private set; get; }

        public GraphQLService(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            IGraphQLMetadataProvider graphQLMetadataProvider,
            IDocumentCache documentCache,
            IDocumentHashProvider documentHashProvider,
            DataGatewayConfig config,
            IRuntimeConfigProvider runtimeConfigProvider,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _graphQLMetadataProvider = graphQLMetadataProvider;
            _config = config;
            _runtimeConfigProvider = runtimeConfigProvider;
            _sqlMetadataProvider = sqlMetadataProvider;
            _documentCache = documentCache;
            _documentHashProvider = documentHashProvider;

            InitializeSchemaAndResolvers();
        }

        private void ParseAsync(DocumentNode root, Dictionary<string, Entity> entities)
        {
            if (_config.DatabaseType == null)
            {
                throw new DataGatewayException("No database type was configured", HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            ISchemaBuilder sb = SchemaBuilder.New()
                .AddDocument(root)
                .AddDirectiveType<ModelDirectiveType>()
                .AddDirectiveType<RelationshipDirectiveType>()
                .AddDirectiveType<PrimaryKeyDirectiveType>()
                .AddDocument(QueryBuilder.Build(root, entities))
                .AddDocument(MutationBuilder.Build(root, _config.DatabaseType.Value, entities));

            Schema = sb
                .AddAuthorizeDirectiveType()
                .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine, _graphQLMetadataProvider))
                .Create();

            // Below is pretty much an inlined version of
            // ISchema.MakeExecutable. The reason that we inline it is that
            // same changes need to be made in the middle of it, such as
            // AddErrorFilter.
            IRequestExecutorBuilder builder = new ServiceCollection()
                .AddGraphQL()
                .AddAuthorization()
                .AddErrorFilter(error =>
            {
                if (error.Exception != null)
                {
                    Console.Error.WriteLine(error.Exception.Message);
                    Console.Error.WriteLine(error.Exception.StackTrace);
                }

                return error;
            })
                .AddErrorFilter(error =>
            {
                if (error.Exception is DataGatewayException)
                {
                    DataGatewayException thrownException = (DataGatewayException)error.Exception;
                    return error.RemoveException()
                            .RemoveLocations()
                            .RemovePath()
                            .WithMessage(thrownException.Message)
                            .WithCode($"{thrownException.SubStatusCode}");
                }

                return error;
            });

            // Sadly IRequestExecutorBuilder.Configure is internal, so we also
            // inline that one here too
            Executor = builder.Services
                .Configure(builder.Name, (RequestExecutorSetup o) => o.Schema = Schema)
                .BuildServiceProvider()
                .GetRequiredService<IRequestExecutorResolver>()
                .GetRequestExecutorAsync()
                .Result;
        }

        /// <summary>
        /// Executes GraphQL request within GraphQL Library components.
        /// </summary>
        /// <param name="requestBody">GraphQL request body</param>
        /// <param name="requestProperties">key/value pairs of properties to be used in GraphQL library pipeline</param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(string requestBody, Dictionary<string, object> requestProperties)
        {
            if (Executor == null)
            {
                /*lang=json,strict*/
                return "{\"error\": \"Schema must be defined first\" }";
            }

            IQueryRequest queryRequest = CompileRequest(requestBody, requestProperties);

            using IExecutionResult result = await Executor.ExecuteAsync(queryRequest);
            return result.ToJson(withIndentations: false);
        }

        /// <summary>
        /// If the metastore provider is able to get the graphql schema,
        /// this function parses it and attaches resolvers to the various query fields.
        /// </summary>
        private void InitializeSchemaAndResolvers()
        {
            if (_config.DatabaseType == null)
            {
                throw new DataGatewayException("No database type was configured", HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            Dictionary<string, Entity> entities = _runtimeConfigProvider.GetRuntimeConfig().Entities;

            DocumentNode root = _config.DatabaseType switch
            {
                DatabaseType.cosmos => GenerateCosmosGraphQLObjects(),
                DatabaseType.mssql or
                DatabaseType.postgresql or
                DatabaseType.mysql => GenerateSqlGraphQLObjects(entities),
                _ => throw new NotImplementedException()
            };

            ParseAsync(root, entities);
        }

        private DocumentNode GenerateSqlGraphQLObjects(Dictionary<string, Entity> entities)
        {
            List<ObjectTypeDefinitionNode> graphQLObjects = new();

            foreach ((string entityName, Entity entity) in entities)
            {
                if (entity.GraphQL is not null)
                {
                    if (entity.GraphQL is bool g && g == false)
                    {
                        continue;
                    }

                    // TODO: Do we need to check the object version of `entity.GraphQL`?
                }

                TableDefinition tableDefinition = _sqlMetadataProvider.GetTableDefinition(entityName);

                ObjectTypeDefinitionNode node = SchemaConverter.FromTableDefinition(entityName, tableDefinition, entity, entities);
                graphQLObjects.Add(node);
            }

            return new DocumentNode(graphQLObjects);
        }

        private DocumentNode GenerateCosmosGraphQLObjects()
        {
            string graphqlSchema = _graphQLMetadataProvider.GetGraphQLSchema();

            if (string.IsNullOrEmpty(graphqlSchema))
            {
                throw new DataGatewayException("No GraphQL object model was provided for CosmosDB. Please define a GraphQL object model and link it in the runtime config.", System.Net.HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            return Utf8GraphQLParser.Parse(graphqlSchema);
        }

        /// <summary>
        /// Adds request properties(i.e. AuthN details) as GraphQL QueryRequest properties so key/values
        /// can be used in HotChocolate Middleware.
        /// </summary>
        /// <param name="requestBody">Http Request Body</param>
        /// <param name="requestProperties">Key/Value Pair of Https Headers intended to be used in GraphQL service</param>
        /// <returns></returns>
        private IQueryRequest CompileRequest(string requestBody, Dictionary<string, object> requestProperties)
        {
            byte[] graphQLData = Encoding.UTF8.GetBytes(requestBody);
            ParserOptions _parserOptions = new();

            Utf8GraphQLRequestParser requestParser = new(
                graphQLData,
                _parserOptions,
                _documentCache,
                _documentHashProvider);

            IReadOnlyList<GraphQLRequest> parsed = requestParser.Parse();

            // TODO: Overhaul this to support batch queries
            // right now we have only assumed a single query/mutation in the request
            // but HotChocolate supports batching and we're just ignoring it for now
            QueryRequestBuilder requestBuilder = QueryRequestBuilder.From(parsed[0]);

            // Individually adds each property to requestBuilder if they are provided.
            // Avoids using SetProperties() as it detrimentally overwrites
            // any properties other Middleware sets.
            if (requestProperties != null)
            {
                foreach (KeyValuePair<string, object> property in requestProperties)
                {
                    requestBuilder.AddProperty(property.Key, property.Value);
                }
            }

            return requestBuilder.Create();
        }
    }
}

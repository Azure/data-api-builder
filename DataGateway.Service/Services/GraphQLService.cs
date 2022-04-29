using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
        private readonly IRuntimeConfigProvider _runtimeConfigProvider;
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly IDocumentCache _documentCache;
        private readonly IDocumentHashProvider _documentHashProvider;
        private readonly bool _useLegacySchema;

        public ISchema? Schema { private set; get; }
        public IRequestExecutor? Executor { private set; get; }

        public GraphQLService(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            IGraphQLMetadataProvider graphQLMetadataProvider,
            IDocumentCache documentCache,
            IDocumentHashProvider documentHashProvider,
            IRuntimeConfigProvider runtimeConfigProvider,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _graphQLMetadataProvider = graphQLMetadataProvider;
            _runtimeConfigProvider = runtimeConfigProvider;
            _sqlMetadataProvider = sqlMetadataProvider;
            _documentCache = documentCache;
            _documentHashProvider = documentHashProvider;

            _useLegacySchema = true;

            InitializeSchemaAndResolvers();
        }

        public void ParseAsync(string data)
        {
            Schema = SchemaBuilder.New()
               .AddDocumentFromString(data)
               .AddAuthorizeDirectiveType()
               .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine, _graphQLMetadataProvider))
               .Create();

            MakeSchemaExecutable();
        }

        /// <summary>
        /// Take the raw GraphQL objects and generate the full schema from them
        /// </summary>
        /// <param name="root">Root document containing the GraphQL object and input types</param>
        /// <param name="inputTypes">Reference table of the input types for query lookup</param>
        /// <param name="entities">Runtime config entities</param>
        /// <exception cref="DataGatewayException">Error will be raised if no database type is set</exception>
        private void ParseAsync(DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes, Dictionary<string, Entity> entities)
        {
            DatabaseType databaseType = _runtimeConfigProvider.GetRuntimeConfig().DataSource.DatabaseType;
            ISchemaBuilder sb = SchemaBuilder.New()
                .AddDocument(root)
                .AddDirectiveType<ModelDirectiveType>()
                .AddDirectiveType<RelationshipDirectiveType>()
                .AddDirectiveType<PrimaryKeyDirectiveType>()
                .AddDocument(QueryBuilder.Build(root, entities, inputTypes))
                .AddDocument(MutationBuilder.Build(root, databaseType, entities));

            Schema = sb
                .AddAuthorizeDirectiveType()
                .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine, _graphQLMetadataProvider))
                .Create();

            MakeSchemaExecutable();
        }

        private void MakeSchemaExecutable()
        {
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
            if (_useLegacySchema)
            {
                // Attempt to get schema from the metadata store.
                string graphqlSchema = _graphQLMetadataProvider.GetGraphQLSchema();

                // If the schema is available, parse it and attach resolvers.
                if (!string.IsNullOrEmpty(graphqlSchema))
                {
                    ParseAsync(graphqlSchema);
                }
            }
            else if (!_useLegacySchema)
            {
                DatabaseType databaseType = _runtimeConfigProvider.GetRuntimeConfig().DataSource.DatabaseType;
                Dictionary<string, Entity> entities = _runtimeConfigProvider.GetRuntimeConfig().Entities;

                (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes) = databaseType switch
                {
                    DatabaseType.cosmos => GenerateCosmosGraphQLObjects(),
                    DatabaseType.mssql or
                    DatabaseType.postgresql or
                    DatabaseType.mysql => GenerateSqlGraphQLObjects(entities),
                    _ => throw new NotImplementedException()
                };

                ParseAsync(root, inputTypes, entities);
            }
        }

        private (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateSqlGraphQLObjects(Dictionary<string, Entity> entities)
        {
            Dictionary<string, ObjectTypeDefinitionNode> objectTypes = new();
            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new();

            // First pass - build up the object and input types for all the entities
            foreach ((string entityName, Entity entity) in entities)
            {
                if (entity.GraphQL is not null)
                {
                    if (entity.GraphQL is bool graphql && graphql == false)
                    {
                        continue;
                    }

                    // TODO: Do we need to check the object version of `entity.GraphQL`?
                }

                TableDefinition tableDefinition = _sqlMetadataProvider.GetTableDefinition(entityName);

                ObjectTypeDefinitionNode node = SchemaConverter.FromTableDefinition(entityName, tableDefinition, entity, entities);
                InputTypeBuilder.GenerateInputTypeForObjectType(node, inputObjects);
                objectTypes.Add(entityName, node);
            }

            // Pass two - Add the arguments to the many-to-* relationship fields
            foreach ((string entityName, Entity entity) in entities)
            {
                if (entity.GraphQL is not null)
                {
                    if (entity.GraphQL is bool graphql && graphql == false)
                    {
                        continue;
                    }

                    // TODO: Do we need to check the object version of `entity.GraphQL`?
                }

                ObjectTypeDefinitionNode node = objectTypes[entityName];
                node = QueryBuilder.AddQueryArgumentsForRelationships(node, entity, inputObjects);
                objectTypes[entityName] = node;
            }

            List<IDefinitionNode> nodes = new(objectTypes.Values);
            return (new DocumentNode(nodes.Concat(inputObjects.Values).ToImmutableList()), inputObjects);
        }

        private (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateCosmosGraphQLObjects()
        {
            string graphqlSchema = _graphQLMetadataProvider.GetGraphQLSchema();

            if (string.IsNullOrEmpty(graphqlSchema))
            {
                throw new DataGatewayException("No GraphQL object model was provided for CosmosDB. Please define a GraphQL object model and link it in the runtime config.", System.Net.HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            return (Utf8GraphQLParser.Parse(graphqlSchema), new Dictionary<string, InputObjectTypeDefinitionNode>());
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
            ParserOptions parserOptions = new();

            Utf8GraphQLRequestParser requestParser = new(
                graphQLData,
                parserOptions,
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

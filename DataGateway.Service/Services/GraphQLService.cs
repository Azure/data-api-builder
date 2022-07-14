using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.GraphQLBuilder.Sql;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services.MetadataProviders;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;

namespace Azure.DataGateway.Service.Services
{
    public class GraphQLService
    {
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly IDocumentCache _documentCache;
        private readonly IDocumentHashProvider _documentHashProvider;
        private readonly DatabaseType _databaseType;
        private readonly Dictionary<string, Entity> _entities;
        private IAuthorizationResolver _authorizationResolver;

        public ISchema? Schema { private set; get; }
        public IRequestExecutor? Executor { private set; get; }
        public ISchemaBuilder? SchemaBuilderObj { private set; get; }
        public DocumentNode? RootDocNode { private set; get; }
        public DocumentNode? QueryDocNode { private set; get; }
        public DocumentNode? MutationDocNode { private set; get; }

        public GraphQLService(
            RuntimeConfigProvider runtimeConfigProvider,
            IDocumentCache documentCache,
            IDocumentHashProvider documentHashProvider,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver
            )
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();

            _databaseType = runtimeConfig.DatabaseType;
            _entities = runtimeConfig.Entities;
            _sqlMetadataProvider = sqlMetadataProvider;
            _documentCache = documentCache;
            _documentHashProvider = documentHashProvider;
            _authorizationResolver = authorizationResolver;

            InitializeSchemaAndResolvers();
        }

        /// <summary>
        /// Take the raw GraphQL objects and generate the full schema from them
        /// </summary>
        /// <param name="root">Root document containing the GraphQL object and input types</param>
        /// <param name="inputTypes">Reference table of the input types for query lookup</param>
        /// <exception cref="DataGatewayException">Error will be raised if no database type is set</exception>
        private void Parse(DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            RootDocNode = root;
            QueryDocNode = QueryBuilder.Build(root, _databaseType, _entities, inputTypes, _authorizationResolver.EntityPermissionsMap);
            MutationDocNode = MutationBuilder.Build(root, _databaseType, _entities, _authorizationResolver.EntityPermissionsMap);
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
            (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes) = _databaseType switch
            {
                DatabaseType.cosmos => GenerateCosmosGraphQLObjects(),
                DatabaseType.mssql or
                DatabaseType.postgresql or
                DatabaseType.mysql => GenerateSqlGraphQLObjects(_entities),
                _ => throw new NotImplementedException($"This database type {_databaseType} is not yet implemented.")
            };

            Parse(root, inputTypes);
        }

        private (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateSqlGraphQLObjects(Dictionary<string, Entity> entities)
        {
            Dictionary<string, ObjectTypeDefinitionNode> objectTypes = new();
            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new();

            // First pass - build up the object and input types for all the entities
            foreach ((string entityName, Entity entity) in entities)
            {
                if (entity.GraphQL is not null && entity.GraphQL is bool graphql && graphql == false)
                {
                    continue;
                }

                TableDefinition tableDefinition = _sqlMetadataProvider.GetTableDefinition(entityName);

                // Collection of role names allowed to access entity, to be added to the authorize directive
                // of the objectTypeDefinitionNode. The authorize Directive is one of many directives created.
                IEnumerable<string> rolesAllowedForEntity = _authorizationResolver.GetRolesForEntity(entityName);

                ObjectTypeDefinitionNode node = SchemaConverter.FromTableDefinition(entityName, tableDefinition, entity, entities, rolesAllowedForEntity);
                InputTypeBuilder.GenerateInputTypesForObjectType(node, inputObjects);
                objectTypes.Add(entityName, node);
            }

            // Pass two - Add the arguments to the many-to-* relationship fields
            foreach ((string entityName, ObjectTypeDefinitionNode node) in objectTypes)
            {
                objectTypes[entityName] = QueryBuilder.AddQueryArgumentsForRelationships(node, inputObjects);
            }

            List<IDefinitionNode> nodes = new(objectTypes.Values);
            return (new DocumentNode(nodes.Concat(inputObjects.Values).ToImmutableList()), inputObjects);
        }

        private (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateCosmosGraphQLObjects()
        {
            string graphqlSchema = ((CosmosSqlMetadataProvider)_sqlMetadataProvider).GraphQLSchema();

            if (string.IsNullOrEmpty(graphqlSchema))
            {
                throw new DataGatewayException(
                    message: "No GraphQL object model was provided for CosmosDB. Please define a GraphQL object model and link it in the runtime config.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new();
            DocumentNode root = Utf8GraphQLParser.Parse(graphqlSchema);

            IEnumerable<ObjectTypeDefinitionNode> objectNodes = root.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>();
            foreach (ObjectTypeDefinitionNode node in objectNodes)
            {
                InputTypeBuilder.GenerateInputTypesForObjectType(node, inputObjects);
            }

            return (root.WithDefinitions(root.Definitions.Concat(inputObjects.Values).ToImmutableList()), inputObjects);
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

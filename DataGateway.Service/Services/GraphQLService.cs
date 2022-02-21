using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataGateway.Services
{
    public class GraphQLService
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly IMetadataStoreProvider _metadataStoreProvider;
        public ISchema? Schema { private set; get; }
        public IRequestExecutor? Executor { private set; get; }

        public GraphQLService(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            IMetadataStoreProvider metadataStoreProvider)
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _metadataStoreProvider = metadataStoreProvider;

            InitializeSchemaAndResolvers();
        }

        public void ParseAsync(string data)
        {
            static bool IsBuiltInType(ITypeNode typeNode)
            {
                string name = typeNode.NamedType().Name.Value;
                if (name == "String" || name == "Int" || name == "Boolean" || name == "Float" || name == "ID")
                {
                    return true;
                }

                return false;
            }

            try
            {
                DocumentNode root = Utf8GraphQLParser.Parse(data);

                SchemaBuilder sb = SchemaBuilder.New();
                sb.AddDocument(root);
                sb.AddDirectiveType(new DirectiveType(config =>
                {
                    config.Name(new NameString("model"));
                    config.Location(HotChocolate.Types.DirectiveLocation.Object);
                }));

                List<FieldDefinitionNode> queryFields = new();
                List<FieldDefinitionNode> mutationFields = new();
                Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs = new();
                List<ObjectTypeDefinitionNode> returnTypes = new();

                foreach (IDefinitionNode definition in root.Definitions)
                {
                    if (definition is ObjectTypeDefinitionNode objectTypeDefinitionNode)
                    {
                        if (objectTypeDefinitionNode.Directives.Any(d => d.Name.ToString() == "model"))
                        {
                            NameNode name = objectTypeDefinitionNode.Name;

                            ObjectTypeDefinitionNode returnType = new(
                                null,
                                new NameNode($"{name}Connection"),
                                null,
                                new List<DirectiveNode>(),
                                new List<NamedTypeNode>(),
                                new List<FieldDefinitionNode>
                                {
                                    new FieldDefinitionNode(
                                        null,
                                        new NameNode("items"),
                                        null,
                                        new List<InputValueDefinitionNode>(),
                                        new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(name)))),
                                        new List<DirectiveNode>()),
                                    new FieldDefinitionNode(
                                        null,
                                        new NameNode("continuation"),
                                        null,
                                        new List<InputValueDefinitionNode>(),
                                        new StringType().ToTypeNode(),
                                        new List<DirectiveNode>())
                                });
                            returnTypes.Add(returnType);

                            queryFields.Add(new FieldDefinitionNode(
                                null,
                                new NameNode($"{name}s"),
                                new StringValueNode($"Get a list of all the {name} items from the database"),
                                new List<InputValueDefinitionNode>
                                {
                                    new InputValueDefinitionNode(null, new NameNode("first"), null, new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                                    new InputValueDefinitionNode(null, new NameNode("continuation"), null, new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                                },
                                new NonNullTypeNode(new NamedTypeNode(returnType.Name)),
                                new List<DirectiveNode>()
                            ));

                            queryFields.Add(new FieldDefinitionNode(
                                null,
                                new NameNode($"{name}_by_pk"),
                                new StringValueNode($"Get a {name} from the database by its ID/primary key"),
                                new List<InputValueDefinitionNode>
                                {
                                    new InputValueDefinitionNode(
                                        null,
                                        new NameNode("id"),
                                        null,
                                        objectTypeDefinitionNode.Fields.First(f => f.Name.Value == "id").Type,
                                        null,
                                        new List<DirectiveNode>())
                                },
                                new NamedTypeNode(name),
                                new List<DirectiveNode>()
                            ));

                            InputObjectTypeDefinitionNode input = GenerateCreateInputType(inputs, objectTypeDefinitionNode, name, root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>());

                            mutationFields.Add(new FieldDefinitionNode(
                                null,
                                new NameNode($"create{name}"),
                                new StringValueNode($"Creates a new {name}"),
                                new List<InputValueDefinitionNode>
                                {
                                    new InputValueDefinitionNode(
                                        null,
                                        new NameNode("item"),
                                        null,
                                        new NonNullTypeNode(new NamedTypeNode(input.Name)),
                                        null,
                                        new List<DirectiveNode>())
                                },
                                new NamedTypeNode(name),
                                new List<DirectiveNode>()
                            ));
                        }
                    }
                }

                List<IDefinitionNode> definitionNodes = new()
                {
                    new ObjectTypeDefinitionNode(null, new NameNode("Query"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), queryFields),
                    new ObjectTypeDefinitionNode(null, new NameNode("Mutation"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), mutationFields),
                };
                definitionNodes.AddRange(inputs.Values);
                definitionNodes.AddRange(returnTypes);
                DocumentNode documentNode = new(definitionNodes);
                sb.AddDocument(documentNode);

                ISchema x = sb
                    .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine, _metadataStoreProvider))
                    .Create();

                //Schema = SchemaBuilder.New()
                //       .AddDocumentFromString(data)
                //       .AddAuthorizeDirectiveType()
                //       .Use((services, next) => new ResolverMiddleware(next, _queryEngine, _mutationEngine, _metadataStoreProvider))
                //       .Create();
                Schema = x;
            }
            catch (Exception)
            {

                throw;
            }

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

            static InputObjectTypeDefinitionNode GenerateCreateInputType(Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, ObjectTypeDefinitionNode objectTypeDefinitionNode, NameNode name, IEnumerable<HotChocolate.Language.IHasName> definitions)
            {
                InputObjectTypeDefinitionNode input = new(
                                                null,
                                                new NameNode($"Create{name}Input"),
                                                new StringValueNode($"Input type for creating {name}"),
                                                new List<DirectiveNode>(),
                                                objectTypeDefinitionNode.Fields
                                                    .Where(f => f.Name.Value != "id")
                                                    .Select(f =>
                                                    {
                                                        if (!IsBuiltInType(f.Type))
                                                        {
                                                            string typeName = f.Type.NamedType().Name.Value;
                                                            HotChocolate.Language.IHasName def = definitions.First(d => d.Name.Value == typeName);
                                                            if (def is ObjectTypeDefinitionNode otdn)
                                                            {
                                                                InputObjectTypeDefinitionNode node;
                                                                if (!inputs.ContainsKey(new NameNode($"Create{typeName}Input")))
                                                                {
                                                                    node = GenerateCreateInputType(inputs, otdn, f.Type.NamedType().Name, definitions);
                                                                }
                                                                else
                                                                {
                                                                    node = inputs[new NameNode($"Create{typeName}Input")];
                                                                }

                                                                return new InputValueDefinitionNode(
                                                                    null,
                                                                    f.Name,
                                                                    new StringValueNode($"Input for field {f.Name} on type Create{name}Input"),
                                                                    new NonNullTypeNode(new NamedTypeNode(node.Name)), // todo - figure out how to properly walk the graph, so you can do [Foo!]!
                                                                    null,
                                                                    f.Directives);
                                                            }
                                                        }

                                                        return new InputValueDefinitionNode(
                                                                null,
                                                                f.Name,
                                                                new StringValueNode($"Input for field {f.Name} on type Create{name}Input"),
                                                                f.Type,
                                                                null,
                                                                f.Directives);

                                                    }).ToList()
                                                );

                inputs.Add(input.Name, input);
                return input;
            }
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
            // Attempt to get schema from the metadata store.
            string graphqlSchema = _metadataStoreProvider.GetGraphQLSchema();

            // If the schema is available, parse it and attach resolvers.
            if (!string.IsNullOrEmpty(graphqlSchema))
            {
                ParseAsync(graphqlSchema);
            }
        }

        /// <summary>
        /// Adds request properties(i.e. AuthN details) as GraphQL QueryRequest properties so key/values
        /// can be used in HotChocolate Middleware.
        /// </summary>
        /// <param name="requestBody">Http Request Body</param>
        /// <param name="requestProperties">Key/Value Pair of Https Headers intended to be used in GraphQL service</param>
        /// <returns></returns>
        private static IQueryRequest CompileRequest(string requestBody, Dictionary<string, object> requestProperties)
        {
            using JsonDocument requestBodyJson = JsonDocument.Parse(requestBody);
            IQueryRequestBuilder requestBuilder = QueryRequestBuilder.New()
                .SetQuery(requestBodyJson.RootElement.GetProperty("query").GetString()!);

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

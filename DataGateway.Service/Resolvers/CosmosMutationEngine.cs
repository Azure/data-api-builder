using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosMutationEngine : IMutationEngine
    {
        private readonly CosmosClientProvider _clientProvider;
        private readonly ISqlMetadataProvider _metadataProvider;

        public CosmosMutationEngine(
            CosmosClientProvider clientProvider,
            ISqlMetadataProvider metadataProvider)
        {
            _clientProvider = clientProvider;
            _metadataProvider = metadataProvider;
        }

        private async Task<JObject> ExecuteAsync(IDictionary<string, object?> queryArgs, CosmosOperationMetadata resolver)
        {
            // TODO: add support for all mutation types
            // we only support CreateOrUpdate (Upsert) for now

            if (queryArgs == null)
            {
                // TODO: in which scenario the queryArgs is empty
                throw new ArgumentNullException(nameof(queryArgs));
            }

            CosmosClient? client = _clientProvider.Client;
            if (client == null)
            {
                throw new DataGatewayException(
                    message: "Cosmos DB has not been properly initialized",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed);
            }

            Container container = client.GetDatabase(resolver.DatabaseName)
                                        .GetContainer(resolver.ContainerName);

            ItemResponse<JObject>? response = resolver.OperationType switch
            {
                Operation.UpdateGraphQL => await HandleUpsertAsync(queryArgs, container),
                Operation.Create => await HandleCreateAsync(queryArgs, container),
                Operation.Delete => await HandleDeleteAsync(queryArgs, container),
                _ => throw new NotSupportedException($"unsupported operation type: {resolver.OperationType}")
            };

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                // Delete item doesnt return the actual item, so we return emtpy json
                return new JObject();
            }

            return response.Resource;
        }

        private static async Task<ItemResponse<JObject>> HandleDeleteAsync(IDictionary<string, object> queryArgs, Container container)
        {
            // TODO: As of now id is the partition key. This has to be changed when partition key support is added. Issue #215
            PartitionKey partitionKey;
            string? id = null;

            if (queryArgs.TryGetValue("id", out object? idObj))
            {
                id = idObj.ToString();
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }
            else
            {
                partitionKey = new(id);
            }

            return await container.DeleteItemAsync<JObject>(id, partitionKey);
        }

        private static async Task<ItemResponse<JObject>> HandleUpsertAsync(IDictionary<string, object> queryArgs, Container container)
        {
            string? id = queryArgs.First(arg => arg.Key == GraphQLUtils.DEFAULT_PRIMARY_KEY_NAME).Value.ToString();

            object item = queryArgs[CreateMutationBuilder.INPUT_ARGUMENT_NAME];

            Dictionary<string, object?> createInput = new();
            if (item is List<ObjectFieldNode> createInputRaw)
            {
                createInput = new Dictionary<string, object?>();
                foreach (ObjectFieldNode node in createInputRaw)
                {
                    createInput.Add(node.Name.Value, node.Value.Value);
                }
            }
            else if (item is Dictionary<string, object?> dict)
            {
                createInput = dict;
            }
            else
            {
                throw new InvalidDataException("The type of argument for the provided data is unsupported.");
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException($"{GraphQLUtils.DEFAULT_PRIMARY_KEY_NAME} field is mandatory");
            }

            createInput.Add(GraphQLUtils.DEFAULT_PRIMARY_KEY_NAME, id);

            return await container.UpsertItemAsync(JObject.FromObject(createInput));
        }

        private static async Task<ItemResponse<JObject>> HandleCreateAsync(IDictionary<string, object> queryArgs, Container container)
        {
            string? id = null;

            object item = queryArgs[CreateMutationBuilder.INPUT_ARGUMENT_NAME];

            // Variables were provided to the mutation
            if (item is Dictionary<string, object?> createInput)
            {
                if (createInput.TryGetValue("id", out object? idObj))
                {
                    id = idObj?.ToString();
                }
            }
            // An inline argument was set
            else if (item is List<ObjectFieldNode> createInputRaw)
            {
                ObjectFieldNode? idObj = createInputRaw.FirstOrDefault(field => field.Name.Value == "id");

                if (idObj != null && idObj.Value.Value != null)
                {
                    id = idObj.Value.Value.ToString();
                }

                createInput = new Dictionary<string, object?>();
                foreach (ObjectFieldNode node in createInputRaw)
                {
                    createInput.Add(node.Name.Value, node.Value.Value);
                }
            }
            else
            {
                throw new InvalidDataException("The type of argument for the provided data is unsupported.");
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }

            return await container.CreateItemAsync(JObject.FromObject(createInput));
        }

        /// <summary>
        /// Executes the mutation query and return result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(IMiddlewareContext context,
            IDictionary<string, object?> parameters)
        {
            string entityName = context.Selection.Field.Type.NamedType().Name.Value;

            string databaseName = _metadataProvider.GetSchemaName(entityName);
            string containerName = _metadataProvider.GetDatabaseObjectName(entityName);

            string graphqlMutationName = context.Selection.Field.Name.Value;
            Operation mutationOperation =
                MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);

            CosmosOperationMetadata mutation = new(databaseName, containerName, mutationOperation);
            // TODO: we are doing multiple round of serialization/deserialization
            // fixme
            JObject jObject = await ExecuteAsync(parameters, mutation);
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            return new Tuple<JsonDocument, IMetadata>(JsonDocument.Parse(jObject.ToString()), null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <returns>JSON object result</returns>
        public Task<JsonDocument?> ExecuteAsync(RestRequestContext context)
        {
            throw new NotImplementedException();
        }
    }
}

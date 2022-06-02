using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosMutationEngine : IMutationEngine
    {
        private readonly CosmosClientProvider _clientProvider;

        private readonly IGraphQLMetadataProvider _metadataStoreProvider;

        public CosmosMutationEngine(CosmosClientProvider clientProvider, IGraphQLMetadataProvider metadataStoreProvider)
        {
            _clientProvider = clientProvider;
            _metadataStoreProvider = metadataStoreProvider;
        }

        private async Task<JObject> ExecuteAsync(IDictionary<string, object?> queryArgs, MutationResolver resolver)
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
                    "Cosmos DB has not been properly initialized",
                    HttpStatusCode.InternalServerError,
                    DataGatewayException.SubStatusCodes.DatabaseOperationFailed);
            }

            Container container = client.GetDatabase(resolver.DatabaseName)
                                        .GetContainer(resolver.ContainerName);

            ItemResponse<JObject>? response;
            switch (resolver.OperationType)
            {
                case Operation.Upsert:
                    response = await HandleUpsertAsync(queryArgs, container);
                    break;
                case Operation.Update:
                    response = await HandleUpdateAsync(queryArgs, container);
                    break;
                case Operation.Delete:
                    response = await HandleDeleteAsync(queryArgs, container);
                    break;
                default:
                    throw new NotSupportedException($"unsupported operation type: {resolver.OperationType}");
            }

            return response.Resource;
        }

        private static async Task<ItemResponse<JObject>> HandleDeleteAsync(IDictionary<string, object> queryArgs, Container container)
        {
            string? partitionKey = null;
            string? id = null;

            if (queryArgs.TryGetValue(QueryBuilder.ID_FIELD_NAME, out object? idObj))
            {
                id = idObj.ToString();
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }

            if (queryArgs.TryGetValue(QueryBuilder.PARTITION_KEY_FIELD_NAME, out object? partitionKeyObj))
            {
                partitionKey = partitionKeyObj.ToString();
            }

            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new InvalidDataException("Partition Key field is mandatory");
            }

            return await container.DeleteItemAsync<JObject>(id, new PartitionKey(partitionKey));
        }

        private static async Task<ItemResponse<JObject>> HandleUpsertAsync(IDictionary<string, object> queryArgs, Container container)
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

            return await container.UpsertItemAsync(JObject.FromObject(createInput));
        }

        private static async Task<ItemResponse<JObject>> HandleUpdateAsync(IDictionary<string, object> queryArgs, Container container)
        {
            string? partitionKey = null;
            string? id = null;

            if (queryArgs.TryGetValue(QueryBuilder.ID_FIELD_NAME, out object? idObj))
            {
                id = idObj.ToString();
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }

            if (queryArgs.TryGetValue(QueryBuilder.PARTITION_KEY_FIELD_NAME, out object? partitionKeyObj))
            {
                partitionKey = partitionKeyObj.ToString();
            }

            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new InvalidDataException("Partition Key field is mandatory");
            }

            object item = queryArgs[CreateMutationBuilder.INPUT_ARGUMENT_NAME];
            JObject? createInput = (JObject?) ParseInputItem(item);

            return await container.ReplaceItemAsync<JObject>(createInput, id, new PartitionKey(partitionKey), new ItemRequestOptions());
        }

        private static object? ParseInputItem(object? item)
        {
            JObject? createInput = new();

            if (item is ObjectFieldNode node)
            {
                createInput.Add(new JProperty(node.Name.Value, ParseInputItem(node.Value.Value)));
                return createInput;
            }

            if (item is List<ObjectFieldNode> nodeList)
            {
                foreach (ObjectFieldNode subfield in nodeList)
                {
                    createInput.Add(new JProperty(subfield.Name.Value, ParseInputItem(subfield.Value.Value)));
                }

                return createInput;
            }

            return item;
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
            string graphQLMutationName = context.Selection.Field.Name.Value;
            MutationResolver resolver = _metadataStoreProvider.GetMutationResolver(graphQLMutationName);
            // TODO: we are doing multiple round of serialization/deserialization
            // fixme
            JObject jObject = await ExecuteAsync(parameters, resolver);
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            return new Tuple<JsonDocument, IMetadata>((jObject is null) ? null! : JsonDocument.Parse(jObject.ToString()), null);
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

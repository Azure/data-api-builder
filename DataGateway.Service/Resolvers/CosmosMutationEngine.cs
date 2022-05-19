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
using Azure.DataGateway.Service.Models;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosMutationEngine : IMutationEngine
    {
        private readonly CosmosClientProvider _clientProvider;

        private readonly IOptionsMonitor<RuntimeConfigPath> _runtimeConfigPath;

        public CosmosMutationEngine(CosmosClientProvider clientProvider, IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath)
        {
            _clientProvider = clientProvider;
            _runtimeConfigPath = runtimeConfigPath;
        }

        private async Task<JObject> ExecuteAsync(IDictionary<string, object?> queryArgs, CosmosMutation resolver)
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
                case Operation.Create:
                    response = await HandleUpsertAsync(queryArgs, container);
                    break;
                case Operation.Delete:
                    response = await HandleDeleteAsync(queryArgs, container);
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        // Delete item doesnt return the actual item, so we return emtpy json
                        return new JObject();
                    }

                    break;
                default:
                    throw new NotSupportedException($"unsupported operation type: {resolver.OperationType}");
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
            RuntimeConfig? configValue = _runtimeConfigPath.CurrentValue.ConfigValue;
            if (configValue is null)
            {
                throw new DataGatewayException("Runtime config isn't setup.", HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            Entity entity = configValue.Entities[entityName];
            string? containerName = (entity.Source is string s && !string.IsNullOrEmpty(s)) ? s : configValue.CosmosDb!.Container;

            if (containerName is null)
            {
                throw new DataGatewayException($"No container configured for entity {entityName}.", HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            string databaseName = configValue.CosmosDb!.Database;

            string graphqlMutationName = context.Selection.Field.Name.Value;
            Operation mutationOperation =
                MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);

            CosmosMutation mutation = new(databaseName, containerName, mutationOperation);
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

    record CosmosMutation(string DatabaseName, string ContainerName, Operation OperationType);
}

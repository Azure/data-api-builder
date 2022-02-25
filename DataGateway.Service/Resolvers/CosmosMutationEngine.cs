using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using HotChocolate.Resolvers;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosMutationEngine : IMutationEngine
    {
        private readonly CosmosClientProvider _clientProvider;

        private readonly IMetadataStoreProvider _metadataStoreProvider;

        public CosmosMutationEngine(CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            _clientProvider = clientProvider;
            _metadataStoreProvider = metadataStoreProvider;
        }

        private async Task<JObject> ExecuteAsync(IDictionary<string, object> inputDict, MutationResolver resolver)
        {
            // TODO: add support for all mutation types
            // we only support CreateOrUpdate (Upsert) for now

            JObject jObject;

            if (inputDict != null)
            {
                // TODO: optimize this multiple round of serialization/deserialization
                string json = JsonConvert.SerializeObject(inputDict);
                jObject = JObject.Parse(json);
            }
            else
            {
                // TODO: in which scenario the inputDict is empty
                throw new NotSupportedException("inputDict is missing");
            }

            Container container = _clientProvider.Client.GetDatabase(resolver.DatabaseName)
                .GetContainer(resolver.ContainerName);
            // TODO: As of now id is the partition key. This has to be changed when partition key support is added. Issue #215
            string id;
            PartitionKey partitionKey;
            if (jObject.TryGetValue("id", out JToken? idObj))
            {
                id = idObj.ToString();
                partitionKey = new(id);
            }
            else
            {
                throw new InvalidDataException("id field is mandatory");
            }

            ItemResponse<JObject>? response;
            switch (resolver.OperationType)
            {
                case Operation.Upsert:
                    response = await container.UpsertItemAsync(jObject);
                    break;
                case Operation.Delete:
                    response = await container.DeleteItemAsync<JObject>(id, partitionKey);
                    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    {
                        // Delete item doesnt return the actual item, so we return emtpy json
                        return new JObject();
                    }

                    break;
                default:
                    throw new NotSupportedException($"unsupprted operation type: {resolver.OperationType.ToString()}");
            }

            return response.Resource;
        }

        /// <summary>
        /// Executes the mutation query and return result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(IMiddlewareContext context,
            IDictionary<string, object> parameters)
        {
            string graphQLMutationName = context.Selection.Field.Name.Value;
            MutationResolver resolver = _metadataStoreProvider.GetMutationResolver(graphQLMutationName);
            // TODO: we are doing multiple round of serialization/deserialization
            // fixme
            JObject jObject = await ExecuteAsync(parameters, resolver);
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

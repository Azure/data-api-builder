using System;
using System.Collections.Generic;
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
            // TODO: check insertion type

            Microsoft.Azure.Cosmos.ItemResponse<JObject> response = await container.UpsertItemAsync(jObject);
            JObject res = response.Resource;
            return res;
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
            return new Tuple<JsonDocument, IMetadata>(JsonDocument.Parse(jObject.ToString()), null);
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">Middleware context of the mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public Task<JsonDocument> ExecuteAsync(RequestContext context)
        {
            throw new NotImplementedException();
        }
    }
}

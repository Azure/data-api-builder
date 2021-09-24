using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Service.Resolvers
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

        /// <summary>
        /// Persists resolver configuration. When resolver config,
        /// is received from REST endpoint and not configuration file.
        /// </summary>
        /// <param name="resolver">The given mutation resolver.</param>
        public void RegisterResolver(MutationResolver resolver)
        {
            // TODO no op for now. remove me
        }

        private async Task<JObject> ExecuteAsync(IDictionary<string, object> inputDict, MutationResolver resolver)
        {
            // TODO: add support for all mutation types
            // we only support CreateOrUpdate (Upsert) for now

            JObject jObject;

            if (inputDict != null)
            {
                // TODO: optimize this multiple round of serialization/deserialization
                var json = JsonConvert.SerializeObject(inputDict);
                jObject = JObject.Parse(json);
            }
            else
            {
                // TODO: in which scenario the inputDict is empty
                throw new NotSupportedException("inputDict is missing");
            }

            var container = _clientProvider.GetClient().GetDatabase(resolver.databaseName)
                .GetContainer(resolver.containerName);
            // TODO: check insertion type

            var response = await container.UpsertItemAsync(jObject);
            JObject res = response.Resource;
            return res;
        }

        /// <summary>
        /// Executes the mutation query and return result as JSON object asynchronously.
        /// </summary>
        /// <param name="graphQLMutationName">name of the GraphQL mutation query.</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<JsonDocument> ExecuteAsync(string graphQLMutationName,
            IDictionary<string, object> parameters)
        {
            var resolver = _metadataStoreProvider.GetMutationResolver(graphQLMutationName);

            // TODO: we are doing multiple round of serialization/deserialization
            // fixme
            JObject jObject = await ExecuteAsync(parameters, resolver);
            return JsonDocument.Parse(jObject.ToString());
        }
    }
}

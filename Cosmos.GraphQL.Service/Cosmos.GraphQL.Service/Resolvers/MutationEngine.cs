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
    public class MutationEngine
    {
        private readonly CosmosClientProvider _clientProvider;
        private readonly IMetadataStoreProvider _metadataStoreProvider;
        
        public MutationEngine(CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._clientProvider = clientProvider;
            this._metadataStoreProvider = metadataStoreProvider;
        }

        public void registerResolver(MutationResolver resolver)
        {
            // TODO: add into system container/rp
            this._metadataStoreProvider.StoreMutationResolver(resolver);
        }

        private JObject execute(IDictionary<string, object> inputDict, MutationResolver resolver)
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

            var container = _clientProvider.getCosmosClient().GetDatabase(resolver.databaseName)
                .GetContainer(resolver.containerName);
            // TODO: check insertion type

            JObject res = container.UpsertItemAsync(jObject).Result.Resource;
            return res;
        }

        public async Task<JsonDocument> execute(string graphQLMutationName,
            IDictionary<string, object> parameters)
        {
            var resolver = _metadataStoreProvider.GetMutationResolver(graphQLMutationName);
            
            // TODO: we are doing multiple round of serialization/deserialization
            // fixme
            JObject jObject = execute(parameters, resolver);
            return JsonDocument.Parse(jObject.ToString());
        }
    }
}
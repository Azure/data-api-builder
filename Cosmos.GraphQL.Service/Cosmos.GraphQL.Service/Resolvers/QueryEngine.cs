using System.Collections.Generic;
using System.Text.Json;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Services
{
    public class QueryEngine
    {
        private readonly CosmosClientProvider _clientProvider;
        private readonly IMetadataStoreProvider _metadataStoreProvider;

        public QueryEngine(CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._clientProvider = clientProvider;
            this._metadataStoreProvider = metadataStoreProvider;
        }

        public void registerResolver(GraphQLQueryResolver resolver)
        {
            this._metadataStoreProvider.StoreQueryResolver(resolver);  
        }

        public JsonDocument execute(string graphQLQueryName, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            var resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            var container = this._clientProvider.getCosmosClient().GetDatabase(resolver.databaseName).GetContainer(resolver.containerName);
            var querySpec = new QueryDefinition(resolver.parametrizedQuery);

            if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
                }
            }

            var firstPage = container.GetItemQueryIterator<JObject>(querySpec).ReadNextAsync().Result;

            JObject firstItem = null;

            var iterator = firstPage.GetEnumerator();

            while (iterator.MoveNext() && firstItem == null)
            {
                firstItem = iterator.Current;
            }
            JsonDocument jsonDocument = JsonDocument.Parse(firstItem.ToString());

            return jsonDocument;
        }

        public IEnumerable<JsonDocument> executeList(string graphQLQueryName, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            var resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            var container = this._clientProvider.getCosmosClient().GetDatabase(resolver.databaseName).GetContainer(resolver.containerName);
            var querySpec = new QueryDefinition(resolver.parametrizedQuery);

            if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
                }
            }

            FeedIterator<JObject> resultSetIterator = container.GetItemQueryIterator<JObject>(querySpec);            

            List<JsonDocument> resultsAsList = new List<JsonDocument>();
            while (resultSetIterator.HasMoreResults)                
            {
                var nextPage = resultSetIterator.ReadNextAsync().Result;
                IEnumerator<JObject> enumerator = nextPage.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    JObject item = enumerator.Current;
                    resultsAsList.Add(JsonDocument.Parse(item.ToString()));
                }
            }

            return resultsAsList;
        }
    }
}
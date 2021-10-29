using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Services
{
    //<summary>
    // CosmosQueryEngine to ExecuteAsync against CosmosDb.
    //</summary>
    public class CosmosQueryEngine : IQueryEngine
    {
        private readonly CosmosClientProvider _clientProvider;
        private readonly IMetadataStoreProvider _metadataStoreProvider;

        // <summary>
        // Constructor.
        // </summary>
        public CosmosQueryEngine(CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._clientProvider = clientProvider;
            this._metadataStoreProvider = metadataStoreProvider;
        }

        // <summary>
        // Register the given resolver with this query engine.
        // </summary>
        public void RegisterResolver(GraphQLQueryResolver resolver)
        {
            this._metadataStoreProvider.StoreQueryResolver(resolver);
        }

        // <summary>
        // ExecuteAsync the given named graphql query on the backend.
        // </summary>
        public async Task<JsonDocument> ExecuteAsync(string graphQLQueryName, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            GraphQLQueryResolver resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            Container container = this._clientProvider.Client.GetDatabase(resolver.DatabaseName).GetContainer(resolver.ContainerName);
            var querySpec = new QueryDefinition(resolver.ParametrizedQuery);

            if (parameters != null)
            {
                foreach (KeyValuePair<string, object> parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
                }
            }

            FeedResponse<JObject> firstPage = await container.GetItemQueryIterator<JObject>(querySpec).ReadNextAsync();

            JObject firstItem = null;

            IEnumerator<JObject> iterator = firstPage.GetEnumerator();

            while (iterator.MoveNext() && firstItem == null)
            {
                firstItem = iterator.Current;
            }

            var jsonDocument = JsonDocument.Parse(firstItem.ToString());

            return jsonDocument;
        }

        public async Task<IEnumerable<JsonDocument>> ExecuteListAsync(string graphQLQueryName, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            GraphQLQueryResolver resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            Container container = this._clientProvider.Client.GetDatabase(resolver.DatabaseName).GetContainer(resolver.ContainerName);
            var querySpec = new QueryDefinition(resolver.ParametrizedQuery);

            if (parameters != null)
            {
                foreach (KeyValuePair<string, object> parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
                }
            }

            FeedIterator<JObject> resultSetIterator = container.GetItemQueryIterator<JObject>(querySpec);

            var resultsAsList = new List<JsonDocument>();
            while (resultSetIterator.HasMoreResults)
            {
                FeedResponse<JObject> nextPage = await resultSetIterator.ReadNextAsync();
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

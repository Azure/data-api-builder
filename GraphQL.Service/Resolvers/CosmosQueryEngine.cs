using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.GraphQL.Service.Models;
using Microsoft.GraphQL.Service.Resolvers;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Microsoft.GraphQL.Services
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

            var resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            var container = this._clientProvider.GetClient().GetDatabase(resolver.databaseName).GetContainer(resolver.containerName);
            var querySpec = new QueryDefinition(resolver.parametrizedQuery);

            if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
                }
            }

            var firstPage = await container.GetItemQueryIterator<JObject>(querySpec).ReadNextAsync();

            JObject firstItem = null;

            var iterator = firstPage.GetEnumerator();

            while (iterator.MoveNext() && firstItem == null)
            {
                firstItem = iterator.Current;
            }
            JsonDocument jsonDocument = JsonDocument.Parse(firstItem.ToString());

            return jsonDocument;
        }

        public async Task<IEnumerable<JsonDocument>> ExecuteListAsync(string graphQLQueryName, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            var resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            var container = this._clientProvider.GetClient().GetDatabase(resolver.databaseName).GetContainer(resolver.containerName);
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
                var nextPage = await resultSetIterator.ReadNextAsync();
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

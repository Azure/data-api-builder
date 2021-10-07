using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Services
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
        public async Task<JsonDocument> ExecuteAsync(string graphQLQueryName, IDictionary<string, object> parameters, bool isContinuationQuery)
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

            var queryRequestOptions = new QueryRequestOptions();
            string requestContinuation = null;
            string responseContinuation = null;
            if (parameters.TryGetValue("first", out object maxSize))
            {
                queryRequestOptions.MaxItemCount = int.Parse(maxSize as string);
            }

            if (parameters.TryGetValue("after", out object after))
            {
                requestContinuation = after as string;
            }

            if (isContinuationQuery)
            {
                List<JObject> resultsAsList = new();
                JArray jarray = new();
                FeedIterator<JObject> resultSetIterator = container.GetItemQueryIterator<JObject>(querySpec, requestContinuation, queryRequestOptions);
                while (resultSetIterator.HasMoreResults)
                {
                    var nextPage = await resultSetIterator.ReadNextAsync();
                    IEnumerator<JObject> enumerator = nextPage.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        JObject item = enumerator.Current;
                        // resultsAsList.Add(JObject.Parse(item.ToString()));
                        //JObject prop = new (item.ToString());
                        jarray.Add(item);
                    }
                    responseContinuation = nextPage.ContinuationToken;
                    break;
                }
                // TODO: Should serialize and prepare response in a better way

                JObject res = new(
                    new JProperty("continuation", responseContinuation),
                    new JProperty("nodes", jarray));

                //string resultJson = "{\"continuation\": " + JsonConvert.SerializeObject(responseContinuation) + ", " +
                //    " \"nodes\": " + JsonConvert.SerializeObject(resultsAsList) + " }";
                JsonDocument resultJsonDoc = JsonDocument.Parse(res.ToString());
                return resultJsonDoc;
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

        public async Task<IEnumerable<JsonDocument>> ExecuteListAsync(string graphQLQueryName, IDictionary<string, object> parameters, bool isContinuationQuery)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            var resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            var container = this._clientProvider.getCosmosClient().GetDatabase(resolver.databaseName).GetContainer(resolver.containerName);
            var querySpec = new QueryDefinition(resolver.parametrizedQuery);
            var queryRequestOptions = new QueryRequestOptions();
            string requestContinuation = null;
            string responseContinuation = null;
            if (parameters.TryGetValue("first", out object maxSize))
            {
                queryRequestOptions.MaxItemCount = maxSize as int?;
            }

            if (parameters.TryGetValue("after", out object after))
            {
                requestContinuation = after as string;
            }


                if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
                }
            }

            FeedIterator<JObject> resultSetIterator = container.GetItemQueryIterator<JObject>(querySpec, requestContinuation, queryRequestOptions);

            List<JsonDocument> resultsAsList = new();

            while (resultSetIterator.HasMoreResults)
            {
                var nextPage = await resultSetIterator.ReadNextAsync();
                IEnumerator<JObject> enumerator = nextPage.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    JObject item = enumerator.Current;
                    resultsAsList.Add(JsonDocument.Parse(item.ToString()));
                }
                responseContinuation = nextPage.ContinuationToken;
            }

            if (isContinuationQuery)
            {
                string resultJson = "{\"continuation\": " + responseContinuation + "}," +
                    " {\"nodes\": " +  resultsAsList.ToString() + " }" ;
                JsonDocument jsonDocument = JsonDocument.Parse(resultJson);
            }

            return resultsAsList;
        }
    }
}

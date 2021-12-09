using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate.Resolvers;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Services
{
    //<summary>
    // CosmosQueryEngine to execute queries against CosmosDb.
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

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json back.
        /// </summary>
        public async Task<JsonDocument> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters, bool isPaginatedQuery)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            string graphQLQueryName = context.Selection.Field.Name.Value;
            GraphQLQueryResolver resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            Container container = this._clientProvider.Client.GetDatabase(resolver.DatabaseName).GetContainer(resolver.ContainerName);

            QueryRequestOptions queryRequestOptions = new();
            string requestContinuation = null;

            QueryDefinition querySpec = new(resolver.ParametrizedQuery);

            if (parameters != null)
            {
                foreach (KeyValuePair<string, object> parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
                }
            }

            if (parameters.TryGetValue("first", out object maxSize))
            {
                queryRequestOptions.MaxItemCount = Convert.ToInt32(maxSize);
            }

            if (parameters.TryGetValue("after", out object after))
            {
                requestContinuation = after as string;
            }

            FeedResponse<JObject> firstPage = await container.GetItemQueryIterator<JObject>(querySpec, requestContinuation, queryRequestOptions).ReadNextAsync();

            if (isPaginatedQuery)
            {
                JArray jarray = new();
                IEnumerator<JObject> enumerator = firstPage.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    JObject item = enumerator.Current;
                    jarray.Add(item);
                }

                string responseContinuation = firstPage.ContinuationToken;
                if (String.IsNullOrEmpty(responseContinuation))
                {
                    responseContinuation = null;
                }

                JObject res = new(
                   new JProperty("endCursor", responseContinuation),
                   new JProperty("hasNextPage", responseContinuation != null),
                   new JProperty("nodes", jarray));

                // This extra deserialize/serialization will be removed after moving to Newtonsoft from System.Text.Json
                JsonDocument resultJsonDoc = JsonDocument.Parse(res.ToString());
                return resultJsonDoc;
            }

            JObject firstItem = null;

            IEnumerator<JObject> iterator = firstPage.GetEnumerator();

            while (iterator.MoveNext() && firstItem == null)
            {
                firstItem = iterator.Current;
            }

            JsonDocument jsonDocument = JsonDocument.Parse(firstItem.ToString());

            return jsonDocument;
        }

        public async Task<IEnumerable<JsonDocument>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            string graphQLQueryName = context.Selection.Field.Name.Value;
            GraphQLQueryResolver resolver = this._metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            Container container = this._clientProvider.Client.GetDatabase(resolver.DatabaseName).GetContainer(resolver.ContainerName);
            QueryDefinition querySpec = new(resolver.ParametrizedQuery);

            if (parameters != null)
            {
                foreach (KeyValuePair<string, object> parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
                }
            }

            FeedIterator<JObject> resultSetIterator = container.GetItemQueryIterator<JObject>(querySpec);

            List<JsonDocument> resultsAsList = new();
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

        // <summary>
        // Given the SqlQueryStructure structure, obtains the query text and executes it against the backend.
        // </summary>
        public Task<JsonDocument> ExecuteAsync(FindRequestContext queryStructure)
        {
            throw new NotImplementedException();
        }
    }
}

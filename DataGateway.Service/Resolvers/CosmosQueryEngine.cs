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
        public async Task<JsonElement> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters, bool isPaginatedQuery)
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
                requestContinuation = Base64Decode(after as string);
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
                if (string.IsNullOrEmpty(responseContinuation))
                {
                    responseContinuation = null;
                }

                JObject res = new(
                   new JProperty("endCursor", Base64Encode(responseContinuation)),
                   new JProperty("hasNextPage", responseContinuation != null),
                   new JProperty("nodes", jarray));

                // This extra deserialize/serialization will be removed after moving to Newtonsoft from System.Text.Json
                using JsonDocument doc = JsonDocument.Parse(res.ToString());
                return doc.RootElement.Clone();
            }

            JObject firstItem = null;

            IEnumerator<JObject> iterator = firstPage.GetEnumerator();

            while (iterator.MoveNext() && firstItem == null)
            {
                firstItem = iterator.Current;
            }

            using JsonDocument jsonDocument = JsonDocument.Parse(firstItem.ToString());
            return jsonDocument.RootElement.Clone();
        }

        public async Task<IEnumerable<JsonElement>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
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

            List<JsonElement> resultsAsList = new();
            while (resultSetIterator.HasMoreResults)
            {
                FeedResponse<JObject> nextPage = await resultSetIterator.ReadNextAsync();
                IEnumerator<JObject> enumerator = nextPage.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    JObject item = enumerator.Current;
                    using JsonDocument doc = JsonDocument.Parse(item.ToString());
                    resultsAsList.Add(doc.RootElement.Clone());
                }
            }

            return resultsAsList;
        }

        // <summary>
        // Given the SqlQueryStructure structure, obtains the query text and executes it against the backend.
        // </summary>
        public Task<JsonElement> ExecuteAsync(FindRequestContext queryStructure)
        {
            throw new NotImplementedException();
        }

        private static string Base64Encode(string plainText)
        {
            if (plainText == default)
            {
                return null;
            }

            byte[] plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        private static string Base64Decode(string base64EncodedData)
        {
            if (base64EncodedData == default)
            {
                return null;
            }

            byte[] base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

    }
}

# nullable disable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate.Resolvers;
using HotChocolate.Types;
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
        private readonly CosmosQueryBuilder _queryBuilder;

        // <summary>
        // Constructor.
        // </summary>
        public CosmosQueryEngine(CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._clientProvider = clientProvider;
            this._metadataStoreProvider = metadataStoreProvider;
            this._queryBuilder = new CosmosQueryBuilder();
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json back.
        /// </summary>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            CosmosQueryStructure structure = new(context, parameters, _metadataStoreProvider);

            Container container = this._clientProvider.Client.GetDatabase(structure.Database).GetContainer(structure.Container);

            QueryRequestOptions queryRequestOptions = new();
            string requestContinuation = null;

            string queryString = _queryBuilder.Build(structure);

            QueryDefinition querySpec = new(queryString);

            foreach (KeyValuePair<string, object> parameterEntry in structure.Parameters)
            {
                querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
            }

            if (structure.IsPaginated)
            {
                queryRequestOptions.MaxItemCount = (int?)structure.MaxItemCount;
                requestContinuation = Base64Decode(structure.Continuation);
            }

            FeedResponse<JObject> firstPage = await container.GetItemQueryIterator<JObject>(querySpec, requestContinuation, queryRequestOptions).ReadNextAsync();

            if (structure.IsPaginated)
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
                   new JProperty("items", jarray));

                // This extra deserialize/serialization will be removed after moving to Newtonsoft from System.Text.Json
                return new Tuple<JsonDocument, IMetadata>(JsonDocument.Parse(res.ToString()), null);
            }

            static JObject FindFirstItem(IEnumerator<JObject> iterator)
            {
                JObject firstItem;
                if (iterator.MoveNext() && (firstItem = iterator.Current) == null)
                {
                    return FindFirstItem(iterator);
                }

                return iterator.Current;
            }

            JObject firstItem = FindFirstItem(firstPage.GetEnumerator());

            JsonDocument jsonDocument = JsonDocument.Parse(firstItem.ToString());

            return new Tuple<JsonDocument, IMetadata>(jsonDocument, null);
        }

        public async Task<Tuple<IEnumerable<JsonDocument>, IMetadata>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            CosmosQueryStructure structure = new(context, parameters, _metadataStoreProvider);

            Container container = this._clientProvider.Client.GetDatabase(structure.Database).GetContainer(structure.Container);
            QueryDefinition querySpec = new(_queryBuilder.Build(structure));

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

            return new Tuple<IEnumerable<JsonDocument>, IMetadata>(resultsAsList, null);
        }

        // <summary>
        // Given the SqlQueryStructure structure, obtains the query text and executes it against the backend.
        // </summary>
        public Task<JsonDocument> ExecuteAsync(RestRequestContext queryStructure)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public JsonDocument ResolveInnerObject(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata)
        {
            //TODO: Try to avoid additional deserialization/serialization here.
            return JsonDocument.Parse(element.ToString());
        }

        /// <inheritdoc />
        public IEnumerable<JsonDocument> ResolveListType(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata)
        {
            //TODO: Try to avoid additional deserialization/serialization here.
            return JsonSerializer.Deserialize<List<JsonDocument>>(element.ToString());
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

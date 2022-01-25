using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
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
                return JsonDocument.Parse(res.ToString());
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
            return LookupById(queryStructure);
        }

        private async Task<JsonDocument> LookupById(FindRequestContext queryStructure)
        {
            TableDefinition tableDefinition = _metadataStoreProvider.GetTableDefinition(queryStructure.EntityName);

            if (tableDefinition == null)
            {
                throw new DatagatewayException(message: "TableDefinition for Entity:" + queryStructure.EntityName + " not found", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
            }

            // for now we only support 
            if (tableDefinition.PrimaryKey.Count != queryStructure.Predicates.Count)
            {
                throw new DatagatewayException(message: "TableDefinition for Entity:" + queryStructure.EntityName + " not found", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
            }

            // id, partitionKey is a pair. for now we are not supporting the hierarchical partition key
            // TODO: assume TableDefinition is validated as per above in the config loading phase.

            System.Diagnostics.Debug.Assert(tableDefinition.PrimaryKey.Count <= 2);
            System.Diagnostics.Debug.Assert(tableDefinition.PrimaryKey.Count >= 1);

            PartitionKey partitionKey = new(queryStructure.Predicates[queryStructure.Predicates.Count - 1].Value);
            Container container = this._clientProvider.Client.GetDatabase(tableDefinition.DatabaseName).GetContainer(tableDefinition.ContainerName);

            using (ResponseMessage responseMessage = await container.ReadItemStreamAsync(queryStructure.Predicates[0].Value, partitionKey))
            {
                // Item stream operations do not throw exceptions for better performance
                if (responseMessage.IsSuccessStatusCode)
                {

                    // TODO: we don't support column push down for point read operation in cosmos db.

                    return JsonDocument.Parse(responseMessage.Content);
                }
                else
                {
                    if (responseMessage.StatusCode.Equals(HttpStatusCode.NotFound))
                    {
                        throw new DatagatewayException(message: "not found", statusCode: 404, DatagatewayException.SubStatusCodes.EntityNotFound);

                    }
                    else
                    {
                        throw new DatagatewayException(message: responseMessage.ErrorMessage, statusCode: 500, DatagatewayException.SubStatusCodes.EntityNotFound);
                    }
                }
            }
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

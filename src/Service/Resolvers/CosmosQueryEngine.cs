# nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    //<summary>
    // CosmosQueryEngine to execute queries against CosmosDb.
    //</summary>
    public class CosmosQueryEngine : IQueryEngine
    {
        private readonly CosmosClientProvider _clientProvider;
        private readonly ISqlMetadataProvider _metadataStoreProvider;
        private readonly CosmosQueryBuilder _queryBuilder;
        private readonly GQLFilterParser _gQLFilterParser;

        // <summary>
        // Constructor.
        // </summary>
        public CosmosQueryEngine(
            CosmosClientProvider clientProvider,
            ISqlMetadataProvider metadataStoreProvider,
            GQLFilterParser gQLFilterParser)
        {
            _clientProvider = clientProvider;
            _metadataStoreProvider = metadataStoreProvider;
            _queryBuilder = new CosmosQueryBuilder();
            _gQLFilterParser = gQLFilterParser;
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json back.
        /// </summary>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            CosmosQueryStructure structure = new(context, parameters, _metadataStoreProvider, _gQLFilterParser);

            string requestContinuation = null;
            string queryString = _queryBuilder.Build(structure);
            QueryDefinition querySpec = new(queryString);
            QueryRequestOptions queryRequestOptions = new();

            Container container = _clientProvider.Client.GetDatabase(structure.Database).GetContainer(structure.Container);
            (string idValue, string partitionKeyValue) = await GetIdAndPartitionKey(parameters, container, structure);

            foreach (KeyValuePair<string, object> parameterEntry in structure.Parameters)
            {
                querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
            }

            if (!string.IsNullOrEmpty(partitionKeyValue))
            {
                queryRequestOptions.PartitionKey = new PartitionKey(partitionKeyValue);
            }

            if (structure.IsPaginated)
            {
                queryRequestOptions.MaxItemCount = (int?)structure.MaxItemCount;
                requestContinuation = Base64Decode(structure.Continuation);
            }

            // If both partition key value and id value are provided, will execute single partition query
            if (!string.IsNullOrEmpty(partitionKeyValue) && !string.IsNullOrEmpty(idValue))
            {
                return await QueryByIdAndPartitionKey(container, idValue, partitionKeyValue, structure.IsPaginated);
            }

            // If partition key value or id values are not provided, will execute cross partition query
            using (FeedIterator<JObject> query = container.GetItemQueryIterator<JObject>(querySpec, requestContinuation, queryRequestOptions))
            {
                do
                {
                    FeedResponse<JObject> page = await query.ReadNextAsync();

                    // For connection type, return first page result directly
                    if (structure.IsPaginated)
                    {
                        JArray jarray = new();
                        IEnumerator<JObject> enumerator = page.GetEnumerator();
                        while (enumerator.MoveNext())
                        {
                            JObject item = enumerator.Current;
                            jarray.Add(item);
                        }

                        string responseContinuation = page.ContinuationToken;
                        if (string.IsNullOrEmpty(responseContinuation))
                        {
                            responseContinuation = null;
                        }

                        JObject res = new(
                            new JProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME, Base64Encode(responseContinuation)),
                            new JProperty(QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME, responseContinuation != null),
                            new JProperty(QueryBuilder.PAGINATION_FIELD_NAME, jarray));

                        // This extra deserialize/serialization will be removed after moving to Newtonsoft from System.Text.Json
                        return new Tuple<JsonDocument, IMetadata>(JsonDocument.Parse(res.ToString()), null);
                    }

                    if (page.Count > 0)
                    {
                        return new Tuple<JsonDocument, IMetadata>(JsonDocument.Parse(page.First().ToString()), null);
                    }
                }
                while (query.HasMoreResults);
            }

            // Return empty list when query gets no result back
            return new Tuple<JsonDocument, IMetadata>(null, null);
        }

        public async Task<Tuple<IEnumerable<JsonDocument>, IMetadata>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            CosmosQueryStructure structure = new(context, parameters, _metadataStoreProvider);

            Container container = _clientProvider.Client.GetDatabase(structure.Database).GetContainer(structure.Container);
            QueryDefinition querySpec = new(_queryBuilder.Build(structure));

            foreach (KeyValuePair<string, object> parameterEntry in structure.Parameters)
            {
                querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value);
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
        public Task<IActionResult> ExecuteAsync(FindRequestContext context)
        {
            throw new NotImplementedException();
        }

        public Task<IActionResult> ExecuteAsync(StoredProcedureRequestContext context)
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

        /// <summary>
        /// Query cosmos container using a single partition key, returns a single document. 
        /// </summary>
        /// <param name="container"></param>
        /// <param name="idValue"></param>
        /// <param name="partitionKeyValue"></param>
        /// <param name="IsPaginated"></param>
        /// <returns></returns>
        private static async Task<Tuple<JsonDocument, IMetadata>> QueryByIdAndPartitionKey(Container container, string idValue, string partitionKeyValue, bool IsPaginated)
        {
            try
            {
                JObject item = await container.ReadItemAsync<JObject>(idValue, new PartitionKey(partitionKeyValue));

                // If paginated, returning a Connection type document.
                if (IsPaginated)
                {
                    JObject res = new(
                        new JProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME, null),
                        new JProperty(QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME, false),
                        new JProperty(QueryBuilder.PAGINATION_FIELD_NAME, new JArray { item }));
                    return new Tuple<JsonDocument, IMetadata>(JsonDocument.Parse(res.ToString()), null);
                }

                return new Tuple<JsonDocument, IMetadata>(JsonDocument.Parse(item.ToString()), null);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new Tuple<JsonDocument, IMetadata>(null, null);
            }
        }

        private async Task<string> GetPartitionKeyPath(Container container)
        {
            string partitionKeyPath = _metadataStoreProvider.GetPartitionKeyPath(container.Database.Id, container.Id);
            if (partitionKeyPath is not null)
            {
                return partitionKeyPath;
            }

            ContainerResponse properties = await container.ReadContainerAsync();
            partitionKeyPath = properties.Resource.PartitionKeyPath;
            _metadataStoreProvider.SetPartitionKeyPath(container.Database.Id, container.Id, partitionKeyPath);

            return partitionKeyPath;
        }

#nullable enable
        private async Task<(string? idValue, string? partitionKeyValue)> GetIdAndPartitionKey(IDictionary<string, object?> parameters, Container container, CosmosQueryStructure structure)
        {
            string? partitionKeyValue = null, idValue = null;
            string partitionKeyPath = await GetPartitionKeyPath(container);

            foreach (KeyValuePair<string, object?> parameterEntry in parameters)
            {
                // id and filter args can't exist at the same time
                if (parameterEntry.Key == QueryBuilder.ID_FIELD_NAME)
                {
                    // Set id value if id is passed in as an argument
                    idValue = parameterEntry.Value?.ToString();
                }
                else if (parameterEntry.Key == QueryBuilder.FILTER_FIELD_NAME)
                {
                    // Mapping partitionKey and id value from filter object if filter keyword exists in args
                    partitionKeyValue = GetPartitionKeyValue(partitionKeyPath, parameterEntry.Value);
                    idValue = GetIdValue(parameterEntry.Value);
                }
            }

            // If partition key was not found in the filter, then check if it's being passed in arguments
            // Partition key is set in the structure object if the _partitionKeyValue keyword exists in args
            if (string.IsNullOrEmpty(partitionKeyValue))
            {
                partitionKeyValue = structure.PartitionKeyValue;
            }

            return new(idValue, partitionKeyValue);
        }

        /// <summary>
        /// This method is using `PartitionKeyPath` to find the partition key value from query input parameters, using recursion.
        /// Example of `PartitionKeyPath` is `/character/id`.
        /// </summary>
        /// <param name="partitionKeyPath"></param>
        /// <param name="parameter"></param>
        /// <returns></returns>
#nullable enable
        private string? GetPartitionKeyValue(string? partitionKeyPath, object? parameter)
        {
            if (parameter is null || partitionKeyPath is null)
            {
                return null;
            }

            string currentEntity = (partitionKeyPath.Split("/").Length > 1) ? partitionKeyPath.Split("/")[1] : string.Empty;

            foreach (ObjectFieldNode item in (IList<ObjectFieldNode>)parameter)
            {
                if (partitionKeyPath == string.Empty
                    && string.Equals(item.Name.Value, "eq", StringComparison.OrdinalIgnoreCase))
                {
                    return item.Value.Value?.ToString();
                }

                if (partitionKeyPath != string.Empty
                    && string.Equals(item.Name.Value, currentEntity, StringComparison.OrdinalIgnoreCase))
                {
                    // Recursion to mapping next inner object
                    int index = partitionKeyPath.IndexOf(currentEntity);
                    string newPartitionKeyPath = partitionKeyPath.Substring(index + currentEntity.Length);
                    return GetPartitionKeyValue(newPartitionKeyPath, item.Value.Value);
                }
            }

            return null;
        }

        /// <summary>
        /// Parsing id field value from input parameter
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>

#nullable enable
        private static string? GetIdValue(object? parameter)
        {
            if (parameter != null)
            {
                foreach (ObjectFieldNode item in (IList<ObjectFieldNode>)parameter)
                {
                    if (string.Equals(item.Name.Value, "id", StringComparison.OrdinalIgnoreCase))
                    {
                        IList<ObjectFieldNode> idValueObj = (IList<ObjectFieldNode>)item.Value.Value;
                        return idValueObj.FirstOrDefault(x => x.Name.Value == "eq")?.Value.Value.ToString();
                    }
                }
            }

            return null;
        }

        private static string Base64Encode(string plainText)
        {
            if (plainText == default)
            {
                return null;
            }

            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        private static string Base64Decode(string base64EncodedData)
        {
            if (base64EncodedData == default)
            {
                return null;
            }

            byte[] base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}

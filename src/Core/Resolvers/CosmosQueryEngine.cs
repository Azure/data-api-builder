// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// CosmosQueryEngine to execute queries against CosmosDb.
    /// </summary>
    public class CosmosQueryEngine : IQueryEngine
    {
        private readonly CosmosClientProvider _clientProvider;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly CosmosQueryBuilder _queryBuilder;
        private readonly GQLFilterParser _gQLFilterParser;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly DabCacheService _cache;

        /// <summary>
        /// Constructor 
        /// </summary>
        public CosmosQueryEngine(
            CosmosClientProvider clientProvider,
            IMetadataProviderFactory metadataProviderFactory,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            RuntimeConfigProvider runtimeConfigProvider,
            DabCacheService cache
            )
        {
            _clientProvider = clientProvider;
            _metadataProviderFactory = metadataProviderFactory;
            _queryBuilder = new CosmosQueryBuilder();
            _gQLFilterParser = gQLFilterParser;
            _authorizationResolver = authorizationResolver;
            _runtimeConfigProvider = runtimeConfigProvider;
            _cache = cache;
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json back.
        /// </summary>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(
            IMiddlewareContext context,
            IDictionary<string, object> parameters,
            string dataSourceName)
        {
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            ISqlMetadataProvider metadataStoreProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);

            CosmosQueryStructure structure = new(context, parameters, metadataStoreProvider, _authorizationResolver, _gQLFilterParser);
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();

            string queryString = _queryBuilder.Build(structure);
            QueryDefinition querySpec = new(queryString);
            QueryRequestOptions queryRequestOptions = new();

            CosmosClient client = _clientProvider.Clients[dataSourceName];
            Container container = client.GetDatabase(structure.Database).GetContainer(structure.Container);
            (string idValue, string partitionKeyValue) = await GetIdAndPartitionKey(context, parameters, container, structure, metadataStoreProvider);

            foreach (KeyValuePair<string, DbConnectionParam> parameterEntry in structure.Parameters)
            {
                querySpec = querySpec.WithParameter(parameterEntry.Key, parameterEntry.Value.Value);
            }

            if (!string.IsNullOrEmpty(partitionKeyValue))
            {
                queryRequestOptions.PartitionKey = new PartitionKey(partitionKeyValue);
            }

            JObject executeQueryResult = null;

            if (runtimeConfig.CanUseCache() && runtimeConfig.Entities[structure.EntityName].IsCachingEnabled)
            {
                StringBuilder dataSourceKey = new(dataSourceName);

                // to support caching for paginated query adding continuation token in the datasource
                dataSourceKey.Append(":");
                dataSourceKey.Append(structure.Continuation);

                DatabaseQueryMetadata queryMetadata = new(queryText: queryString, dataSource: dataSourceKey.ToString(), queryParameters: structure.Parameters);

                executeQueryResult = await _cache.GetOrSetAsync<JObject>(async () => await ExecuteQueryAsync(structure, querySpec, queryRequestOptions, container, idValue, partitionKeyValue), queryMetadata, runtimeConfig.GetEntityCacheEntryTtl(entityName: structure.EntityName));
            }
            else
            {
                executeQueryResult = await ExecuteQueryAsync(structure, querySpec, queryRequestOptions, container, idValue, partitionKeyValue);
            }

            JsonDocument response = executeQueryResult != null ? JsonDocument.Parse(executeQueryResult.ToString()) : null;

            return new Tuple<JsonDocument, IMetadata>(response, null);
        }

        /// <summary>
        /// ExecuteQueryAsync Performs single partition and cross partition queries. 
        /// </summary>
        /// <param name="structure">CosmosQueryStructure</param>
        /// <param name="querySpec">QueryDefinition defining a Cosmos SQL Query</param>
        /// <param name="queryRequestOptions">The Cosmos query request options</param>
        /// <param name="container">CosmosDB Container</param>
        /// <param name="idValue">Id param</param>
        /// <param name="partitionKeyValue">PartitionKey Value</param>
        /// <returns>JObject</returns>
        private static async Task<JObject> ExecuteQueryAsync(
            CosmosQueryStructure structure,
            QueryDefinition querySpec,
            QueryRequestOptions queryRequestOptions,
            Container container,
            string idValue,
            string partitionKeyValue)
        {
            string requestContinuation = null;
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

                        return res;
                    }

                    if (page.Count > 0)
                    {
                        return page.First();
                    }
                }
                while (query.HasMoreResults);
            }

            // Return null when query gets no result back
            return null;
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a list of Json back.
        /// </summary>
        public async Task<Tuple<IEnumerable<JsonDocument>, IMetadata>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters, string dataSourceName)
        {
            // TODO: fixme we have multiple rounds of serialization/deserialization JsomDocument/JObject
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            ISqlMetadataProvider metadataStoreProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
            CosmosQueryStructure structure = new(context, parameters, metadataStoreProvider, _authorizationResolver, _gQLFilterParser);
            CosmosClient client = _clientProvider.Clients[dataSourceName];
            Container container = client.GetDatabase(structure.Database).GetContainer(structure.Container);
            QueryDefinition querySpec = new(_queryBuilder.Build(structure));

            foreach (KeyValuePair<string, DbConnectionParam> parameterEntry in structure.Parameters)
            {
                querySpec = querySpec.WithParameter(parameterEntry.Key, parameterEntry.Value.Value);
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

        /// <inheritdoc />
        public Task<JsonDocument> ExecuteAsync(FindRequestContext context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public Task<IActionResult> ExecuteAsync(StoredProcedureRequestContext context, string dataSourceName)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public JsonElement ResolveObject(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata)
        {
            return element;
        }

        /// <inheritdoc />
        /// metadata is not used in this method, but it is required by the interface.
        public object ResolveList(JsonElement array, IObjectField fieldSchema, ref IMetadata metadata)
        {
            IType listType = fieldSchema.Type;
            // Is the List type nullable? [...]! vs [...]
            if (listType.IsNonNullType())
            {
                listType = listType.InnerType().InnerType();
            }
            else
            {
                listType = listType.InnerType();
            }

            // Is the type of the list values nullable?
            if (listType.IsNonNullType())
            {
                listType = listType.InnerType();
            }

            if (listType.IsObjectType())
            {
                return JsonSerializer.Deserialize<List<JsonElement>>(array);
            }

            return JsonSerializer.Deserialize(array, fieldSchema.RuntimeType);
        }

        /// <summary>
        /// Query cosmos container using a single partition key, returns a single document. 
        /// </summary>
        /// <param name="container"></param>
        /// <param name="idValue"></param>
        /// <param name="partitionKeyValue"></param>
        /// <param name="IsPaginated"></param>
        /// <returns></returns>
        private static async Task<JObject> QueryByIdAndPartitionKey(Container container, string idValue, string partitionKeyValue, bool IsPaginated)
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
                    return res;
                }

                return item;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        private static async Task<string> GetPartitionKeyPath(Container container, ISqlMetadataProvider metadataStoreProvider)
        {
            string partitionKeyPath = metadataStoreProvider.GetPartitionKeyPath(container.Database.Id, container.Id);
            if (partitionKeyPath is not null)
            {
                return partitionKeyPath;
            }

            ContainerResponse properties = await container.ReadContainerAsync();
            partitionKeyPath = properties.Resource.PartitionKeyPath;
            metadataStoreProvider.SetPartitionKeyPath(container.Database.Id, container.Id, partitionKeyPath);

            return partitionKeyPath;
        }

#nullable enable

        /// <summary>
        /// Resolve partition key and id value from input parameters.
        /// </summary>
        /// <param name="context">Provide the information about variables and filters</param>
        /// <param name="parameters">Contains argument information such as id, filter</param>
        /// <param name="container">Container instance to get the container properties such as partition path</param>
        /// <param name="structure">Fallback to get partition path information</param>
        /// <param name="metadataStoreProvider">Set partition key path, fetched from container properties</param>
        /// <returns></returns>
        private static async Task<(string? idValue, string? partitionKeyValue)> GetIdAndPartitionKey(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            Container container,
            CosmosQueryStructure structure,
            ISqlMetadataProvider metadataStoreProvider)
        {
            string? partitionKeyValue = null, idValue = null;
            string partitionKeyPath = await GetPartitionKeyPath(container, metadataStoreProvider);

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
                    partitionKeyValue = GetPartitionKeyValue(context, partitionKeyPath, parameterEntry.Value);
                    idValue = GetIdValue(context, parameterEntry.Value);
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
        private static string? GetPartitionKeyValue(IMiddlewareContext context, string? partitionKeyPath, object? parameter)
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
                    return ExecutionHelper.ExtractValueFromIValueNode(
                        item.Value,
                        context.Selection.Field.Arguments[QueryBuilder.FILTER_FIELD_NAME],
                        context.Variables)?.ToString();
                }

                if (partitionKeyPath != string.Empty
                    && string.Equals(item.Name.Value, currentEntity, StringComparison.OrdinalIgnoreCase))
                {
                    // Recursion to mapping next inner object
                    int index = partitionKeyPath.IndexOf(currentEntity);
                    string newPartitionKeyPath = partitionKeyPath[(index + currentEntity.Length)..partitionKeyPath.Length];
                    return GetPartitionKeyValue(context, newPartitionKeyPath, item.Value.Value);
                }
            }

            return null;
        }

        /// <summary>
        /// Parsing id field value from input parameter
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        private static string? GetIdValue(IMiddlewareContext context, object? parameter)
        {
            if (parameter != null)
            {
                foreach (ObjectFieldNode item in (IList<ObjectFieldNode>)parameter)
                {
                    if (string.Equals(item.Name.Value, "id", StringComparison.OrdinalIgnoreCase))
                    {
                        IList<ObjectFieldNode>? idValueObj = (IList<ObjectFieldNode>?)item.Value.Value;

                        ObjectFieldNode? itemToResolve = idValueObj?.FirstOrDefault(x => x.Name.Value == "eq");
                        if (itemToResolve is null)
                        {
                            return null;
                        }

                        return ExecutionHelper.ExtractValueFromIValueNode(
                            itemToResolve.Value,
                            context.Selection.Field.Arguments[QueryBuilder.FILTER_FIELD_NAME],
                            context.Variables)?
                            .ToString();
                    }
                }
            }

            return null;
        }

        private static string? Base64Encode(string plainText)
        {
            if (plainText == default)
            {
                return null;
            }

            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        private static string? Base64Decode(string base64EncodedData)
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

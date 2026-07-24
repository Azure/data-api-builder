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
using Azure.DataApiBuilder.Service.GraphQLBuilder;
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

        // Maps relationship-field filter operators to their Cosmos SQL predicate templates ({0}=field, {1}=param).
        private static readonly IReadOnlyDictionary<string, string> _relationshipFilterTemplates =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["eq"] = "c.{0} = {1}",
                ["ne"] = "c.{0} != {1}",
                ["neq"] = "c.{0} != {1}",
                ["gt"] = "c.{0} > {1}",
                ["gte"] = "c.{0} >= {1}",
                ["lt"] = "c.{0} < {1}",
                ["lte"] = "c.{0} <= {1}",
                ["contains"] = "CONTAINS(c.{0}, {1})",
                ["notContains"] = "NOT CONTAINS(c.{0}, {1})",
                ["startsWith"] = "STARTSWITH(c.{0}, {1})",
                ["endsWith"] = "ENDSWITH(c.{0}, {1})",
            };

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
            // TODO: add support for TOP and Order-by push-down

            ISqlMetadataProvider metadataStoreProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);

            CosmosQueryStructure structure = new(context, parameters, _runtimeConfigProvider, metadataStoreProvider, _authorizationResolver, _gQLFilterParser);
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

            if (runtimeConfig.CanUseCache() && runtimeConfig.IsEntityCachingEnabled(structure.EntityName))
            {
                StringBuilder dataSourceKey = new(dataSourceName);

                // to support caching for paginated query adding continuation token in the datasource
                dataSourceKey.Append(":");
                dataSourceKey.Append(structure.Continuation);

                DatabaseQueryMetadata queryMetadata = new(queryText: queryString, dataSource: dataSourceKey.ToString(), queryParameters: structure.Parameters);

                executeQueryResult = await _cache.GetOrSetAsync<JObject>(async () => await ExecuteQueryAsync(structure, querySpec, queryRequestOptions, container, idValue, partitionKeyValue), queryMetadata, runtimeConfig.GetEntityCacheEntryTtl(entityName: structure.EntityName), runtimeConfig.GetEntityCacheEntryLevel(entityName: structure.EntityName));
            }
            else
            {
                executeQueryResult = await ExecuteQueryAsync(structure, querySpec, queryRequestOptions, container, idValue, partitionKeyValue);
            }

            // Resolve relationships for the single entity result
            if (executeQueryResult != null)
            {
                // Check if this is a paginated result (connection type)
                if (structure.IsPaginated && executeQueryResult[QueryBuilder.PAGINATION_FIELD_NAME] is JArray itemsArray)
                {
                    // Resolve relationships for items in the pagination result
                    List<JObject> itemsList = itemsArray.Cast<JObject>().ToList();
                    await ResolveRelationshipsAsync(context, itemsList, structure.EntityName, metadataStoreProvider);
                    // Items are modified in place, no need to update the array
                }
                else
                {
                    // Single entity result
                    List<JObject> resultsList = new() { executeQueryResult };
                    await ResolveRelationshipsAsync(context, resultsList, structure.EntityName, metadataStoreProvider);
                    executeQueryResult = resultsList[0];
                }
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
            // TODO: add support for TOP and Order-by push-down

            ISqlMetadataProvider metadataStoreProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
            CosmosQueryStructure structure = new(context, parameters, _runtimeConfigProvider, metadataStoreProvider, _authorizationResolver, _gQLFilterParser);
            CosmosClient client = _clientProvider.Clients[dataSourceName];
            Container container = client.GetDatabase(structure.Database).GetContainer(structure.Container);
            QueryDefinition querySpec = new(_queryBuilder.Build(structure));

            foreach (KeyValuePair<string, DbConnectionParam> parameterEntry in structure.Parameters)
            {
                querySpec = querySpec.WithParameter(parameterEntry.Key, parameterEntry.Value.Value);
            }

            FeedIterator<JObject> resultSetIterator = container.GetItemQueryIterator<JObject>(querySpec);

            List<JObject> resultsAsList = new();
            while (resultSetIterator.HasMoreResults)
            {
                FeedResponse<JObject> nextPage = await resultSetIterator.ReadNextAsync();
                IEnumerator<JObject> enumerator = nextPage.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    JObject item = enumerator.Current;
                    resultsAsList.Add(item);
                }
            }

            // Resolve relationships (cross-container queries)
            await ResolveRelationshipsAsync(context, resultsAsList, structure.EntityName, metadataStoreProvider);

            // Convert JObject list to JsonDocument list
            // Serialize the entire array at once to properly preserve nested type information
            JArray jArray = new(resultsAsList);
            using (MemoryStream ms = new())
            using (StreamWriter writer = new(ms, System.Text.Encoding.UTF8, 1024, leaveOpen: true))
            using (Newtonsoft.Json.JsonTextWriter jsonWriter = new(writer))
            {
                jArray.WriteTo(jsonWriter);
                jsonWriter.Flush();
                writer.Flush();
                ms.Position = 0;
                
                // Deserialize as array of JsonDocuments
                JsonDocument arrayDoc = JsonDocument.Parse(ms);
                List<JsonDocument> jsonDocumentList = new();
                foreach (JsonElement element in arrayDoc.RootElement.EnumerateArray())
                {
                    // Clone each element as its own JsonDocument
                    jsonDocumentList.Add(JsonDocument.Parse(element.GetRawText()));
                }

                arrayDoc.Dispose();
                
                return new Tuple<IEnumerable<JsonDocument>, IMetadata>(jsonDocumentList, null);
            }
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
        public JsonElement ResolveObject(JsonElement element, ObjectField fieldSchema, ref IMetadata metadata)
        {
            return element;
        }

        /// <inheritdoc />
        /// metadata is not used in this method, but it is required by the interface.
        public object ResolveList(JsonElement array, ObjectField fieldSchema, ref IMetadata metadata)
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

            // For primitive arrays, manually enumerate and extract values to avoid JsonElement wrappers
            List<object> result = new();
            foreach (JsonElement element in array.EnumerateArray())
            {
                result.Add(UnwrapJsonElement(element));
            }

            return result;
        }

        /// <summary>
        /// Unwraps a JsonElement to its underlying primitive value
        /// </summary>
        private static object UnwrapJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt32(out int intVal) ? intVal :
                                       element.TryGetInt64(out long longVal) ? longVal :
                                       element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element // Return as-is for objects/arrays
            };
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

        private static object? GetScalarValue(IValueNode valueNode)
        {
            return valueNode switch
            {
                StringValueNode stringValue => stringValue.Value,
                IntValueNode intValue => int.Parse(intValue.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture),
                FloatValueNode floatValue => double.Parse(floatValue.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture),
                BooleanValueNode boolValue => boolValue.Value,
                NullValueNode => null,
                _ => valueNode.ToString()
            };
        }

        private static void CollectRequestedFields(
            SelectionSetNode selectionSet,
            DocumentNode document,
            Dictionary<string, FieldNode> requestedFields)
        {
            foreach (var selection in selectionSet.Selections)
            {
                switch (selection)
                {
                    case FieldNode fieldNode:
                        // Only add if not already present (fragment fields can be duplicated)
                        if (!requestedFields.ContainsKey(fieldNode.Name.Value))
                        {
                            requestedFields[fieldNode.Name.Value] = fieldNode;
                        }

                        break;
                    case FragmentSpreadNode fragmentSpread:
                        var fragment = document.Definitions
                            .OfType<FragmentDefinitionNode>()
                            .FirstOrDefault(f => f.Name.Value == fragmentSpread.Name.Value);
                        if (fragment != null)
                        {
                            CollectRequestedFields(fragment.SelectionSet, document, requestedFields);
                        }

                        break;
                    case InlineFragmentNode inlineFragment:
                        CollectRequestedFields(inlineFragment.SelectionSet, document, requestedFields);
                        break;
                }
            }
        }

        /// <summary>
        /// Resolves relationships by executing cross-container queries for related entities.
        /// Extracts the selection set from the middleware context and delegates to the internal method.
        /// </summary>
        private async Task ResolveRelationshipsAsync(
            IMiddlewareContext context,
            List<JObject> results,
            string entityName,
            ISqlMetadataProvider metadataProvider)
        {
            if (results.Count == 0)
            {
                return;
            }

            // Get the selection set to find which relationship fields are requested
            SelectionSetNode? selectionSet = context.Selection.RequireFieldNode().SelectionSet;
            if (selectionSet == null)
            {
                return;
            }

            // For paginated queries, the selection set contains "items" which contains the actual fields
            // We need to look inside the "items" field to find relationship fields
            FieldNode? itemsField = selectionSet.Selections
                .OfType<FieldNode>()
                .FirstOrDefault(f => f.Name.Value == QueryBuilder.PAGINATION_FIELD_NAME);

            if (itemsField?.SelectionSet != null)
            {
                selectionSet = itemsField.SelectionSet;
            }

            await ResolveRelationshipsInternalAsync(selectionSet, context.Operation.Document, results, entityName, metadataProvider);
        }

        /// <summary>
        /// Recursively resolves relationships by executing cross-container queries for related entities.
        /// Mutates the input results list to include related entity data, including nested relationships.
        /// </summary>
        private async Task ResolveRelationshipsInternalAsync(
            SelectionSetNode selectionSet,
            DocumentNode document,
            List<JObject> results,
            string entityName,
            ISqlMetadataProvider metadataProvider)
        {
            if (results.Count == 0)
            {
                return;
            }

            // Get entity configuration
            RuntimeConfig config = _runtimeConfigProvider.GetConfig();
            if (!config.Entities.TryGetValue(entityName, out Entity? entity))
            {
                return;
            }

            Dictionary<string, FieldNode> requestedFields = new();
            CollectRequestedFields(selectionSet, document, requestedFields);

            // 1. Resolve configured relationships as cross-container joins.
            if (entity.Relationships is not null)
            {
                foreach ((string fieldName, FieldNode fieldNode) in requestedFields)
                {
                    if (entity.Relationships.TryGetValue(fieldName, out EntityRelationship? relationship))
                    {
                        await ResolveRelationshipFieldAsync(
                            fieldNode, fieldName, relationship, entityName, metadataProvider, config, document, results);
                    }
                }
            }

            // 2. Descend into nested sub-objects that map to configured entities with relationships.
            await ResolveNestedEntityRelationshipsAsync(requestedFields, entity, config, document, results);
        }

        /// <summary>
        /// Resolves a single configured relationship field by querying the target container for the
        /// related rows and grafting them onto each source row according to the relationship cardinality.
        /// </summary>
        private async Task ResolveRelationshipFieldAsync(
            FieldNode fieldNode,
            string fieldName,
            EntityRelationship relationship,
            string entityName,
            ISqlMetadataProvider metadataProvider,
            RuntimeConfig config,
            DocumentNode document,
            List<JObject> results)
        {
            string targetEntityName = relationship.TargetEntity.Split('.').Last();
            if (!config.Entities.ContainsKey(targetEntityName))
            {
                return;
            }

            // The target entity may live in a different data source (and Cosmos account).
            string targetDataSourceName = config.GetDataSourceNameFromEntityName(targetEntityName);
            if (!_clientProvider.Clients.TryGetValue(targetDataSourceName, out CosmosClient? targetClient) || targetClient is null)
            {
                return;
            }

            ISqlMetadataProvider targetMetadataProvider = _metadataProviderFactory.GetMetadataProvider(targetDataSourceName);

            HashSet<string> sourceValues = CollectSourceFieldValues(results, relationship, entityName, metadataProvider);
            if (sourceValues.Count == 0)
            {
                return;
            }

            string? targetField = relationship.TargetFields?.FirstOrDefault();
            if (string.IsNullOrEmpty(targetField))
            {
                return;
            }

            if (!targetMetadataProvider.TryGetExposedColumnName(targetEntityName, targetField, out string? exposedTargetField))
            {
                exposedTargetField = targetField;
            }

            Container container = targetClient
                .GetDatabase(targetMetadataProvider.GetSchemaName(targetEntityName))
                .GetContainer(targetMetadataProvider.GetDatabaseObjectName(targetEntityName));

            QueryDefinition queryDef = BuildRelatedEntitiesQuery(
                exposedTargetField, sourceValues, fieldNode, targetEntityName, targetMetadataProvider);
            Dictionary<string, List<JObject>> relatedEntitiesByKey =
                await ExecuteAndGroupByFieldAsync(container, queryDef, exposedTargetField);

            AssignRelatedEntities(results, relationship, fieldName, entityName, metadataProvider, relatedEntitiesByKey);

            // Recurse into the related rows to resolve their own relationships.
            // JObject is a reference type, so these mutations are reflected in the already-assigned fields.
            if (fieldNode.SelectionSet is not null && relatedEntitiesByKey.Count > 0)
            {
                List<JObject> allRelated = relatedEntitiesByKey.Values.SelectMany(v => v).ToList();
                await ResolveRelationshipsInternalAsync(fieldNode.SelectionSet, document, allRelated, targetEntityName, targetMetadataProvider);
            }
        }

        /// <summary>
        /// Resolves relationships defined on configured entities that appear as nested sub-objects of the
        /// current results (e.g. a "general" sub-document whose "General" entity declares its own relationships).
        /// </summary>
        private async Task ResolveNestedEntityRelationshipsAsync(
            Dictionary<string, FieldNode> requestedFields,
            Entity entity,
            RuntimeConfig config,
            DocumentNode document,
            List<JObject> results)
        {
            foreach ((string fieldName, FieldNode fieldNode) in requestedFields)
            {
                // Skip fields already handled as top-level relationships or that have no sub-selection.
                if ((entity.Relationships is not null && entity.Relationships.ContainsKey(fieldName))
                    || fieldNode.SelectionSet is null)
                {
                    continue;
                }

                string? matchedEntityName = FindEntityWithRelationshipsBySingularName(config, fieldName);
                if (matchedEntityName is null)
                {
                    continue;
                }

                List<JObject> nestedObjects = ExtractNestedObjects(results, fieldName);
                if (nestedObjects.Count == 0)
                {
                    continue;
                }

                string nestedDataSourceName = config.GetDataSourceNameFromEntityName(matchedEntityName);
                ISqlMetadataProvider nestedMetadataProvider = _metadataProviderFactory.GetMetadataProvider(nestedDataSourceName);
                await ResolveRelationshipsInternalAsync(fieldNode.SelectionSet, document, nestedObjects, matchedEntityName, nestedMetadataProvider);
            }
        }

        /// <summary>
        /// Collects the distinct values of the relationship's (first) source field across the given rows,
        /// using the exposed (GraphQL) name of the backing column.
        /// </summary>
        private static HashSet<string> CollectSourceFieldValues(
            List<JObject> results,
            EntityRelationship relationship,
            string entityName,
            ISqlMetadataProvider metadataProvider)
        {
            HashSet<string> values = new();
            string? sourceField = relationship.SourceFields?.FirstOrDefault();
            if (string.IsNullOrEmpty(sourceField))
            {
                return values;
            }

            if (!metadataProvider.TryGetExposedColumnName(entityName, sourceField, out string? exposedSourceField))
            {
                exposedSourceField = sourceField;
            }

            foreach (JObject result in results)
            {
                if (result.TryGetValue(exposedSourceField, out JToken? sourceValue) && sourceValue is not null)
                {
                    values.Add(sourceValue.ToString());
                }
            }

            return values;
        }

        /// <summary>
        /// Builds the Cosmos query that fetches the related rows: a parameterized IN clause over the
        /// target field, plus any operator filters supplied on the relationship field argument.
        /// </summary>
        private static QueryDefinition BuildRelatedEntitiesQuery(
            string exposedTargetField,
            IReadOnlyCollection<string> sourceValues,
            FieldNode relationshipField,
            string targetEntityName,
            ISqlMetadataProvider targetMetadataProvider)
        {
            List<KeyValuePair<string, object?>> parameters = new();
            StringBuilder queryText = new($"SELECT * FROM c WHERE c.{exposedTargetField} IN (");

            int index = 0;
            foreach (string value in sourceValues)
            {
                string paramName = $"@value{index}";
                queryText.Append(index == 0 ? paramName : $", {paramName}");
                parameters.Add(new(paramName, value));
                index++;
            }

            queryText.Append(')');

            // Apply optional operator filters declared on the relationship field.
            ArgumentNode? filterArg = relationshipField.Arguments
                .FirstOrDefault(arg => arg.Name.Value == QueryBuilder.FILTER_FIELD_NAME);
            if (filterArg?.Value is ObjectValueNode filterObject)
            {
                foreach (ObjectFieldNode filterField in filterObject.Fields)
                {
                    if (filterField.Value is not ObjectValueNode operatorObject)
                    {
                        continue;
                    }

                    if (!targetMetadataProvider.TryGetExposedColumnName(targetEntityName, filterField.Name.Value, out string? exposedFieldName))
                    {
                        exposedFieldName = filterField.Name.Value;
                    }

                    foreach (ObjectFieldNode operatorField in operatorObject.Fields)
                    {
                        if (!_relationshipFilterTemplates.TryGetValue(operatorField.Name.Value, out string? template))
                        {
                            continue;
                        }

                        string paramName = $"@filter{parameters.Count}";
                        queryText.Append(" AND ").AppendFormat(template, exposedFieldName, paramName);
                        parameters.Add(new(paramName, GetScalarValue(operatorField.Value)));
                    }
                }
            }

            QueryDefinition queryDef = new(queryText.ToString());
            foreach ((string name, object? value) in parameters)
            {
                queryDef = queryDef.WithParameter(name, value);
            }

            return queryDef;
        }

        /// <summary>
        /// Executes the related-entities query and groups the returned rows by the value of the target field.
        /// </summary>
        private static async Task<Dictionary<string, List<JObject>>> ExecuteAndGroupByFieldAsync(
            Container container,
            QueryDefinition queryDef,
            string exposedTargetField)
        {
            Dictionary<string, List<JObject>> grouped = new();
            using FeedIterator<JObject> iterator = container.GetItemQueryIterator<JObject>(queryDef);
            while (iterator.HasMoreResults)
            {
                FeedResponse<JObject> page = await iterator.ReadNextAsync();
                foreach (JObject item in page)
                {
                    if (item.TryGetValue(exposedTargetField, out JToken? targetValue) && targetValue is not null)
                    {
                        string key = targetValue.ToString();
                        if (!grouped.TryGetValue(key, out List<JObject>? bucket))
                        {
                            bucket = new();
                            grouped[key] = bucket;
                        }

                        bucket.Add(item);
                    }
                }
            }

            return grouped;
        }

        /// <summary>
        /// Grafts the grouped related rows onto each source row. For Cardinality.One the related row is
        /// assigned directly; otherwise a pagination connection object wrapping the related rows is assigned.
        /// </summary>
        private static void AssignRelatedEntities(
            List<JObject> results,
            EntityRelationship relationship,
            string fieldName,
            string entityName,
            ISqlMetadataProvider metadataProvider,
            Dictionary<string, List<JObject>> relatedEntitiesByKey)
        {
            string? sourceField = relationship.SourceFields?.FirstOrDefault();
            if (string.IsNullOrEmpty(sourceField))
            {
                return;
            }

            if (!metadataProvider.TryGetExposedColumnName(entityName, sourceField, out string? exposedSourceField))
            {
                exposedSourceField = sourceField;
            }

            foreach (JObject result in results)
            {
                if (!result.TryGetValue(exposedSourceField, out JToken? sourceValue) || sourceValue is null)
                {
                    continue;
                }

                relatedEntitiesByKey.TryGetValue(sourceValue.ToString(), out List<JObject>? relatedEntities);

                if (relationship.Cardinality == Cardinality.One)
                {
                    result[fieldName] = relatedEntities is { Count: > 0 } ? relatedEntities[0] : null;
                }
                else
                {
                    result[fieldName] = new JObject(
                        new JProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME, (string?)null),
                        new JProperty(QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME, false),
                        new JProperty(QueryBuilder.PAGINATION_FIELD_NAME, relatedEntities is null ? new JArray() : new JArray(relatedEntities)));
                }
            }
        }

        /// <summary>
        /// Finds a configured entity whose GraphQL singular name matches the field and which declares relationships.
        /// </summary>
        private static string? FindEntityWithRelationshipsBySingularName(RuntimeConfig config, string fieldName)
        {
            foreach ((string name, Entity candidate) in config.Entities)
            {
                if (string.Equals(candidate.GraphQL.Singular, fieldName, StringComparison.OrdinalIgnoreCase)
                    && candidate.Relationships is { Count: > 0 })
                {
                    return name;
                }
            }

            return null;
        }

        /// <summary>
        /// Extracts nested JObject values for a field from the given rows, flattening arrays of objects.
        /// </summary>
        private static List<JObject> ExtractNestedObjects(List<JObject> results, string fieldName)
        {
            List<JObject> nestedObjects = new();
            foreach (JObject result in results)
            {
                if (!result.TryGetValue(fieldName, out JToken? nestedValue))
                {
                    continue;
                }

                if (nestedValue is JObject nestedObj)
                {
                    nestedObjects.Add(nestedObj);
                }
                else if (nestedValue is JArray nestedArray)
                {
                    nestedObjects.AddRange(nestedArray.OfType<JObject>());
                }
            }

            return nestedObjects;
        }
    }
}

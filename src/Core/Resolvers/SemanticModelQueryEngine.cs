// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.OData.UriParser;
using OrderBy = Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.OrderBy;
using QueryBuilder = Azure.DataApiBuilder.Service.GraphQLBuilder.Queries.QueryBuilder;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Query engine for Semantic Models (Analysis Services / Power BI / Fabric).
    /// Builds DAX queries from REST/GraphQL request contexts and executes them
    /// via the SemanticModelQueryExecutor.
    /// </summary>
    public class SemanticModelQueryEngine : IQueryEngine
    {
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly IAbstractQueryManagerFactory _queryManagerFactory;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly ILogger<IQueryEngine> _logger;

        public SemanticModelQueryEngine(
            IAbstractQueryManagerFactory queryManagerFactory,
            IMetadataProviderFactory metadataProviderFactory,
            RuntimeConfigProvider runtimeConfigProvider,
            ILogger<IQueryEngine> logger)
        {
            _queryManagerFactory = queryManagerFactory;
            _metadataProviderFactory = metadataProviderFactory;
            _runtimeConfigProvider = runtimeConfigProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string dataSourceName)
        {
            // Resolve entity name from the GraphQL output type (not the field name).
            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
            string entityName = GraphQLUtils.GetEntityNameFromContext(context);
            string tableName = metadataProvider.GetDatabaseObjectName(entityName);

            DaxQueryStructure queryStructure = BuildQueryStructureFromParameters(tableName, parameters, dataSourceName);

            // Extract requested fields from the GraphQL selection set to push down to DAX.
            // This avoids evaluating expensive measures that aren't needed.
            HashSet<string>? requestedFields = ExtractRequestedFields(context);

            // Populate columns and measures, optionally filtered to requested fields.
            // Relationship FK columns are always included to support relationship resolution.
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            Entity entity = runtimeConfig.Entities[entityName];
            HashSet<string>? fkColumns = GetEntityFkColumns(entity);
            PopulateColumnsAndMeasures(queryStructure, entityName, metadataProvider, requestedFields, fkColumns);

            string daxQuery = DaxQueryBuilder.Build(queryStructure);

            IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(DatabaseType.SemanticModel);

            JsonArray? result = await queryExecutor.ExecuteQueryAsync<JsonArray>(
                daxQuery,
                new Dictionary<string, DbConnectionParam>(),
                async (reader, args) => await queryExecutor.GetJsonArrayAsync(reader, args),
                dataSourceName);

            // Resolve relationship fields by batch-fetching related entities.
            if (result is not null && result.Count > 0 && entity.Relationships is not null)
            {
                await ResolveRelationshipsAsync(result, entity, entityName, metadataProvider, queryExecutor, dataSourceName, context);
            }

            // Build a pagination connection object: { "items": "[...]", "hasNextPage": false }
            // HotChocolate's list field resolver expects this structure for connection types.
            JsonObject connection = new();
            string itemsJson = result is not null ? result.ToJsonString() : "[]";
            connection.Add("items", itemsJson);
            connection.Add("hasNextPage", false);

            JsonDocument doc = JsonDocument.Parse(connection.ToJsonString());
            return new Tuple<JsonDocument?, IMetadata?>(doc, null);
        }

        /// <inheritdoc/>
        public async Task<Tuple<IEnumerable<JsonDocument>, IMetadata?>> ExecuteListAsync(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string dataSourceName)
        {
            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
            string entityName = GraphQLUtils.GetEntityNameFromContext(context);
            string tableName = metadataProvider.GetDatabaseObjectName(entityName);

            DaxQueryStructure queryStructure = BuildQueryStructureFromParameters(tableName, parameters, dataSourceName);

            PopulateColumnsAndMeasures(queryStructure, entityName, metadataProvider);

            string daxQuery = DaxQueryBuilder.Build(queryStructure);

            IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(DatabaseType.SemanticModel);

            JsonArray? result = await queryExecutor.ExecuteQueryAsync<JsonArray>(
                daxQuery,
                new Dictionary<string, DbConnectionParam>(),
                async (reader, args) => await queryExecutor.GetJsonArrayAsync(reader, args),
                dataSourceName);

            List<JsonDocument> documents = new();
            if (result is not null)
            {
                foreach (JsonNode? item in result)
                {
                    if (item is not null)
                    {
                        documents.Add(JsonDocument.Parse(item.ToJsonString()));
                    }
                }
            }

            return new Tuple<IEnumerable<JsonDocument>, IMetadata?>(documents, null);
        }

        /// <inheritdoc/>
        public async Task<JsonDocument?> ExecuteAsync(FindRequestContext context)
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            string entityName = context.EntityName;
            string dataSourceName = runtimeConfig.DefaultDataSourceName;
            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);

            // Use the actual database table name from the entity source configuration, not the entity name.
            string tableName = metadataProvider.GetDatabaseObjectName(entityName);

            DaxQueryStructure queryStructure = new()
            {
                TableName = tableName
            };

            // Apply primary key filters if any
            if (context.PrimaryKeyValuePairs is not null && context.PrimaryKeyValuePairs.Count > 0)
            {
                foreach (KeyValuePair<string, object> pk in context.PrimaryKeyValuePairs)
                {
                    string value = pk.Value is string s ? $"\"{s}\"" : pk.Value.ToString() ?? "BLANK()";
                    queryStructure.FilterPredicates.Add(
                        DaxQueryBuilder.BuildEqualityFilter(tableName, pk.Key, value));
                }

                queryStructure.TopCount = 1;
            }

            // Apply field selection — separate columns from measures.
            // Always use SELECTCOLUMNS to produce clean column names
            // (without the table prefix that DAX adds by default).
            HashSet<string> entityMeasureNames = GetEntityMeasureNames(entityName, metadataProvider);

            if (context.FieldsToBeReturned is not null && context.FieldsToBeReturned.Count > 0)
            {
                foreach (string field in context.FieldsToBeReturned)
                {
                    if (entityMeasureNames.Contains(field))
                    {
                        string originalName = GetMeasureOriginalName(field, metadataProvider);
                        queryStructure.IncludedMeasures[field] = $"[{originalName}]";
                    }
                    else
                    {
                        string originalName = GetColumnOriginalName(field, metadataProvider);
                        queryStructure.SelectedColumns[field] = originalName;
                    }
                }
            }
            else
            {
                // Use all columns and measures from the entity's source definition.
                PopulateColumnsAndMeasures(queryStructure, entityName, metadataProvider);
            }

            // Apply $filter — translate OData filter AST to DAX filter predicates.
            if (context.FilterClauseInUrl is not null)
            {
                DaxODataASTVisitor visitor = new(tableName, entityName, metadataProvider);
                string daxFilter = context.FilterClauseInUrl.Expression.Accept(visitor);
                if (!string.IsNullOrEmpty(daxFilter))
                {
                    queryStructure.FilterPredicates.Add(daxFilter);
                }
            }

            // Apply $orderby — translate parsed OrderByColumns to DAX ORDER BY.
            if (context.OrderByClauseOfBackingColumns is not null && context.OrderByClauseOfBackingColumns.Count > 0)
            {
                foreach (OrderByColumn orderCol in context.OrderByClauseOfBackingColumns)
                {
                    queryStructure.OrderByColumns.Add((orderCol.ColumnName, orderCol.Direction == OrderBy.ASC));
                }
            }

            // Apply pagination — request exactly the page size limit.
            // Semantic models don't have primary keys, so cursor-based pagination (nextLink) isn't supported.
            if (context.PrimaryKeyValuePairs is null || context.PrimaryKeyValuePairs.Count == 0)
            {
                uint paginationLimit = runtimeConfig.GetPaginationLimit(context.First);
                queryStructure.TopCount = (int)paginationLimit;
            }

            string daxQuery = DaxQueryBuilder.Build(queryStructure);

            _logger.LogDebug("Executing DAX query: {DaxQuery}", daxQuery);

            IQueryExecutor queryExecutor = _queryManagerFactory.GetQueryExecutor(DatabaseType.SemanticModel);

            JsonArray? result = await queryExecutor.ExecuteQueryAsync<JsonArray>(
                daxQuery,
                new Dictionary<string, DbConnectionParam>(),
                async (reader, args) => await queryExecutor.GetJsonArrayAsync(reader, args),
                dataSourceName);

            if (result is null || result.Count == 0)
            {
                return null;
            }

            // Return the result array directly — the REST pipeline adds the "value" wrapper.
            return JsonDocument.Parse(result.ToJsonString());
        }

        /// <inheritdoc/>
        public Task<IActionResult> ExecuteAsync(StoredProcedureRequestContext context, string dataSourceName)
        {
            throw new DataApiBuilderException(
                message: "Stored procedures are not supported for semantic models.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
        }

        /// <inheritdoc/>
        public JsonElement ResolveObject(JsonElement element, ObjectField fieldSchema, ref IMetadata metadata)
        {
            return element;
        }

        /// <inheritdoc/>
        public object ResolveList(JsonElement array, ObjectField fieldSchema, ref IMetadata? metadata)
        {
            List<JsonElement> resolvedList = new();

            if (array.ValueKind is JsonValueKind.Array)
            {
                foreach (JsonElement element in array.EnumerateArray())
                {
                    resolvedList.Add(element);
                }
            }
            else if (array.ValueKind is JsonValueKind.String)
            {
                // items stored as serialized JSON string in the connection object.
                using JsonDocument parsed = JsonDocument.Parse(array.GetString()!);
                foreach (JsonElement element in parsed.RootElement.EnumerateArray())
                {
                    resolvedList.Add(element.Clone());
                }
            }

            return resolvedList;
        }

        /// <summary>
        /// Builds a DaxQueryStructure from query parameters.
        /// Handles GraphQL pagination parameters (first, after) separately from entity field filters.
        /// </summary>
        private static DaxQueryStructure BuildQueryStructureFromParameters(
            string entityName,
            IDictionary<string, object?> parameters,
            string dataSourceName)
        {
            DaxQueryStructure structure = new()
            {
                TableName = entityName
            };

            foreach (KeyValuePair<string, object?> param in parameters)
            {
                if (param.Value is null)
                {
                    continue;
                }

                // Handle GraphQL pagination parameters.
                if (string.Equals(param.Key, QueryBuilder.PAGE_START_ARGUMENT_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    if (param.Value is int intVal)
                    {
                        structure.TopCount = intVal;
                    }
                    else if (int.TryParse(param.Value.ToString(), out int parsed))
                    {
                        structure.TopCount = parsed;
                    }

                    continue;
                }

                if (string.Equals(param.Key, QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    // Cursor-based pagination not supported for semantic models.
                    continue;
                }

                // Treat remaining parameters as entity field equality filters.
                string value = param.Value is string s ? $"\"{s}\"" : param.Value.ToString() ?? "BLANK()";
                structure.FilterPredicates.Add(
                    DaxQueryBuilder.BuildEqualityFilter(entityName, param.Key, value));
            }

            return structure;
        }

        /// <summary>
        /// Populates SelectedColumns and IncludedMeasures on the query structure
        /// by splitting all entity fields into columns (go into SELECTCOLUMNS) and
        /// measures (go into ADDCOLUMNS with measure reference).
        /// </summary>
        private static void PopulateColumnsAndMeasures(
            DaxQueryStructure queryStructure,
            string entityName,
            ISqlMetadataProvider metadataProvider,
            HashSet<string>? requestedFields = null,
            HashSet<string>? fkColumns = null)
        {
            SourceDefinition sourceDef = metadataProvider.GetSourceDefinition(entityName);
            HashSet<string> measureNames = GetEntityMeasureNames(entityName, metadataProvider);

            foreach (string fieldName in sourceDef.Columns.Keys)
            {
                if (measureNames.Contains(fieldName))
                {
                    // Only include measures if they're requested (or no filter specified).
                    if (requestedFields is not null && !requestedFields.Contains(fieldName))
                    {
                        continue;
                    }

                    // Measure reference: use original name for DAX (may differ from sanitized GraphQL name).
                    string originalName = GetMeasureOriginalName(fieldName, metadataProvider);
                    queryStructure.IncludedMeasures[fieldName] = $"[{originalName}]";
                }
                else
                {
                    // Always include columns (needed for FK resolution and filtering).
                    // Column reference: use original name for DAX column reference,
                    // sanitized name as the alias in the result set.
                    string originalName = GetColumnOriginalName(fieldName, metadataProvider);
                    queryStructure.SelectedColumns[fieldName] = originalName;
                }
            }
        }

        /// <summary>
        /// Returns the set of measure names for the given entity from the metadata provider.
        /// Names are sanitized GraphQL-safe identifiers.
        /// </summary>
        private static HashSet<string> GetEntityMeasureNames(
            string entityName,
            ISqlMetadataProvider metadataProvider)
        {
            if (metadataProvider is SemanticModelMetadataProvider smProvider)
            {
                return smProvider.GetEntityMeasures(entityName);
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the original semantic model measure name for a sanitized GraphQL field name.
        /// </summary>
        private static string GetMeasureOriginalName(
            string graphQLName,
            ISqlMetadataProvider metadataProvider)
        {
            if (metadataProvider is SemanticModelMetadataProvider smProvider)
            {
                return smProvider.GetMeasureOriginalName(graphQLName);
            }

            return graphQLName;
        }

        /// <summary>
        /// Returns the original semantic model column name for a sanitized GraphQL field name.
        /// </summary>
        private static string GetColumnOriginalName(
            string graphQLName,
            ISqlMetadataProvider metadataProvider)
        {
            if (metadataProvider is SemanticModelMetadataProvider smProvider)
            {
                return smProvider.GetColumnOriginalName(graphQLName);
            }

            return graphQLName;
        }

        /// <summary>
        /// Extracts the set of field names requested in the GraphQL selection set.
        /// Walks the connection → items → fields AST to find the actual field names.
        /// Returns null if the fields cannot be determined (fallback to include all).
        /// </summary>
        private static HashSet<string>? ExtractRequestedFields(IMiddlewareContext context)
        {
            var connectionSelections = context.Selection.SyntaxNode.SelectionSet?.Selections;
            if (connectionSelections is null)
            {
                return null;
            }

            foreach (var sel in connectionSelections)
            {
                if (sel is HotChocolate.Language.FieldNode fieldNode && fieldNode.Name.Value == "items")
                {
                    var itemSelections = fieldNode.SelectionSet?.Selections;
                    if (itemSelections is null)
                    {
                        return null;
                    }

                    HashSet<string> fields = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var itemSel in itemSelections)
                    {
                        if (itemSel is HotChocolate.Language.FieldNode itemField)
                        {
                            fields.Add(itemField.Name.Value);
                        }
                    }

                    return fields.Count > 0 ? fields : null;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the set of FK column names from the entity's relationships.
        /// These columns must always be included in the DAX query for relationship resolution.
        /// </summary>
        private static HashSet<string>? GetEntityFkColumns(Entity entity)
        {
            if (entity.Relationships is null || entity.Relationships.Count == 0)
            {
                return null;
            }

            HashSet<string> fkColumns = new(StringComparer.OrdinalIgnoreCase);
            foreach ((_, EntityRelationship relationship) in entity.Relationships)
            {
                if (relationship.SourceFields is not null)
                {
                    foreach (string field in relationship.SourceFields)
                    {
                        fkColumns.Add(field);
                    }
                }
            }

            return fkColumns.Count > 0 ? fkColumns : null;
        }

        /// <summary>
        /// Resolves relationship fields on the result array by batch-fetching related entities.
        /// For each configured relationship:
        /// - Collects FK values from the source results
        /// - Executes a single DAX query for the target entity filtered to those FK values
        /// - Injects the related entity as a nested JSON property on each source item
        /// </summary>
        private static async Task ResolveRelationshipsAsync(
            JsonArray result,
            Entity entity,
            string entityName,
            ISqlMetadataProvider metadataProvider,
            IQueryExecutor queryExecutor,
            string dataSourceName,
            IMiddlewareContext? context = null)
        {
            if (entity.Relationships is null)
            {
                return;
            }

            // Determine which relationship fields are actually requested in the GraphQL query
            // to avoid unnecessary DAX queries for unrequested relationships.
            HashSet<string> requestedFields = new(StringComparer.OrdinalIgnoreCase);
            if (context is not null)
            {
                // Walk the selection AST: connection → items → fields to find relationship field names.
                var connectionSelections = context.Selection.SyntaxNode.SelectionSet?.Selections;
                if (connectionSelections is not null)
                {
                    foreach (var sel in connectionSelections)
                    {
                        Console.Error.WriteLine($"[SM-REL] Connection sel: {sel.GetType().Name} = {(sel as HotChocolate.Language.FieldNode)?.Name.Value ?? "?"}");
                        if (sel is HotChocolate.Language.FieldNode fieldNode && fieldNode.Name.Value == "items")
                        {
                            var itemSelections = fieldNode.SelectionSet?.Selections;
                            if (itemSelections is not null)
                            {
                                foreach (var itemSel in itemSelections)
                                {
                                    if (itemSel is HotChocolate.Language.FieldNode itemField)
                                    {
                                        requestedFields.Add(itemField.Name.Value);
                                    }
                                }
                            }

                            break;
                        }
                    }
                }
            }

            foreach ((string relationshipName, EntityRelationship relationship) in entity.Relationships)
            {
                try
                {
                // Only resolve relationships that the GraphQL query actually requests.
                if (requestedFields.Count > 0 && !requestedFields.Contains(relationshipName))
                {
                    continue;
                }
                string targetEntityName = relationship.TargetEntity;
                string? sourceField = relationship.SourceFields?.FirstOrDefault();
                string? targetField = relationship.TargetFields?.FirstOrDefault();

                if (string.IsNullOrEmpty(sourceField) || string.IsNullOrEmpty(targetField))
                {
                    continue;
                }

                string targetTableName = metadataProvider.GetDatabaseObjectName(targetEntityName);

                // Collect unique FK values from the source results.
                HashSet<string> fkValues = new();
                foreach (JsonNode? item in result)
                {
                    if (item is JsonObject obj && obj.TryGetPropertyValue(sourceField, out JsonNode? fkNode) && fkNode is not null)
                    {
                        string fkStr = fkNode.GetValue<JsonElement>().ValueKind == JsonValueKind.String
                            ? fkNode.GetValue<string>() ?? string.Empty
                            : fkNode.ToJsonString();
                        if (!string.IsNullOrEmpty(fkStr))
                        {
                            fkValues.Add(fkStr);
                        }
                    }
                }

                if (fkValues.Count == 0)
                {
                    continue;
                }

                // Build a DAX query for the target entity filtered to the FK values.
                DaxQueryStructure targetQuery = new() { TableName = targetTableName };
                PopulateColumnsAndMeasures(targetQuery, targetEntityName, metadataProvider);

                // Resolve the original column name for DAX references (SourceFields/TargetFields
                // use sanitized names matching JSON keys, but DAX needs the original model names).
                string targetFieldForDax = GetColumnOriginalName(targetField, metadataProvider);

                // Determine the target column's data type to handle Date/DateTime comparisons properly.
                // DAX cannot compare Date columns with Text values directly.
                bool isDateColumn = false;
                SourceDefinition targetSourceDef = metadataProvider.GetSourceDefinition(targetEntityName);
                if (targetSourceDef.Columns.TryGetValue(targetField, out ColumnDefinition? targetColDef))
                {
                    isDateColumn = targetColDef.SystemType == typeof(DateTime);
                }

                // Build an OR filter for all FK values.
                List<string> filterParts = new();
                foreach (string fkVal in fkValues)
                {
                    string quotedVal;
                    if (isDateColumn)
                    {
                        // Parse ISO date string and use DAX DATE() function for proper type comparison.
                        if (DateTime.TryParse(fkVal, out DateTime dateVal))
                        {
                            quotedVal = $"DATE({dateVal.Year}, {dateVal.Month}, {dateVal.Day})";
                        }
                        else
                        {
                            quotedVal = $"DATEVALUE(\"{fkVal}\")";
                        }
                    }
                    else if (long.TryParse(fkVal, out _) || double.TryParse(fkVal, out _))
                    {
                        quotedVal = fkVal;
                    }
                    else
                    {
                        quotedVal = $"\"{fkVal}\"";
                    }

                    filterParts.Add($"{DaxQueryBuilder.QuoteTableName(targetTableName)}{DaxQueryBuilder.QuoteColumnName(targetFieldForDax)} = {quotedVal}");
                }

                if (filterParts.Count == 1)
                {
                    targetQuery.FilterPredicates.Add(filterParts[0]);
                }
                else
                {
                    targetQuery.FilterPredicates.Add(string.Join(" || ", filterParts));
                }

                string targetDax = DaxQueryBuilder.Build(targetQuery);

                JsonArray? relatedResults = await queryExecutor.ExecuteQueryAsync<JsonArray>(
                    targetDax,
                    new Dictionary<string, DbConnectionParam>(),
                    async (reader, args) => await queryExecutor.GetJsonArrayAsync(reader, args),
                    dataSourceName);

                if (relatedResults is null || relatedResults.Count == 0)
                {
                    continue;
                }

                // Build a lookup from target FK value → related entity JSON.
                if (relationship.Cardinality == Cardinality.One)
                {
                    // Many-to-One: each source item maps to at most one target.
                    Dictionary<string, JsonNode> lookup = new(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonNode? relItem in relatedResults)
                    {
                        if (relItem is JsonObject relObj && relObj.TryGetPropertyValue(targetField, out JsonNode? keyNode) && keyNode is not null)
                        {
                            string key = keyNode.GetValue<JsonElement>().ValueKind == JsonValueKind.String
                                ? keyNode.GetValue<string>() ?? string.Empty
                                : keyNode.ToJsonString();
                            lookup.TryAdd(key, relObj);
                        }
                    }

                    // Inject the related entity onto each source item.
                    foreach (JsonNode? item in result)
                    {
                        if (item is JsonObject srcObj && srcObj.TryGetPropertyValue(sourceField, out JsonNode? fkNode) && fkNode is not null)
                        {
                            string fk = fkNode.GetValue<JsonElement>().ValueKind == JsonValueKind.String
                                ? fkNode.GetValue<string>() ?? string.Empty
                                : fkNode.ToJsonString();
                            if (lookup.TryGetValue(fk, out JsonNode? relatedEntity))
                            {
                                srcObj[relationshipName] = JsonNode.Parse(relatedEntity.ToJsonString());
                            }
                        }
                    }
                }
                else
                {
                    // One-to-Many: each source item maps to multiple targets.
                    Dictionary<string, List<JsonNode>> lookup = new(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonNode? relItem in relatedResults)
                    {
                        if (relItem is JsonObject relObj && relObj.TryGetPropertyValue(targetField, out JsonNode? keyNode) && keyNode is not null)
                        {
                            string key = keyNode.GetValue<JsonElement>().ValueKind == JsonValueKind.String
                                ? keyNode.GetValue<string>() ?? string.Empty
                                : keyNode.ToJsonString();
                            if (!lookup.TryGetValue(key, out List<JsonNode>? list))
                            {
                                list = new List<JsonNode>();
                                lookup[key] = list;
                            }

                            list.Add(relObj);
                        }
                    }

                    foreach (JsonNode? item in result)
                    {
                        if (item is JsonObject srcObj && srcObj.TryGetPropertyValue(sourceField, out JsonNode? fkNode) && fkNode is not null)
                        {
                            string fk = fkNode.GetValue<JsonElement>().ValueKind == JsonValueKind.String
                                ? fkNode.GetValue<string>() ?? string.Empty
                                : fkNode.ToJsonString();
                            if (lookup.TryGetValue(fk, out List<JsonNode>? relatedEntities))
                            {
                                // For connection type resolution, wrap as { "items": "[...]", "hasNextPage": false }
                                JsonArray relArray = new();
                                foreach (JsonNode relNode in relatedEntities)
                                {
                                    relArray.Add(JsonNode.Parse(relNode.ToJsonString()));
                                }

                                JsonObject connObj = new();
                                connObj.Add("items", relArray.ToJsonString());
                                connObj.Add("hasNextPage", false);
                                srcObj[relationshipName] = connObj;
                            }
                        }
                    }
                }

                }
                catch (Exception)
                {
                    // Skip failed relationship resolution (e.g., DAX type mismatch) to avoid
                    // blocking the main entity query. The error is already logged by the executor.
                    continue;
                }
            }
        }
    }
}



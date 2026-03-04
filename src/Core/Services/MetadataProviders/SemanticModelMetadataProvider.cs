// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using HotChocolate.Language;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    /// <summary>
    /// Metadata provider for Semantic Models (Analysis Services / Power BI).
    /// Connects via ADOMD.NET to discover schema metadata (tables, columns, measures, relationships).
    /// Many SQL-specific operations are not supported and throw <see cref="NotSupportedException"/>.
    /// </summary>
    public class SemanticModelMetadataProvider : ISqlMetadataProvider
    {
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly RuntimeEntities _runtimeConfigEntities;
        private readonly bool _isDevelopmentMode;
        private readonly DatabaseType _databaseType;
        private readonly string _connectionString;
        private readonly ILogger? _logger;
        private ODataParser? _odataParser;

        /// <summary>
        /// Maps GraphQL singular type names to entity names defined in the runtime config.
        /// </summary>
        private readonly Dictionary<string, string> _graphQLTypeToEntityNameMap = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps entity name to its column name→column name identity mapping (exposed == backing for semantic models).
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, string>> _entityToFieldMappings = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Model-wide measure registry: measure name → MeasureDefinition.
        /// All measures from all tables are stored here; home table is metadata, not a constraint.
        /// </summary>
        private readonly Dictionary<string, MeasureDefinition> _measureRegistry = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Per-entity set of measure names that are exposed as virtual fields on that entity.
        /// Populated during InitializeAsync based on entity config ("measures" property).
        /// Stores sanitized GraphQL-safe names (which may differ from original measure names).
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> _entityMeasures = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps sanitized GraphQL measure field name → original measure name from the semantic model.
        /// Used when building DAX queries, which require the original measure name (e.g., [Margin %]).
        /// Only contains entries where the sanitized name differs from the original.
        /// </summary>
        private readonly Dictionary<string, string> _measureGraphQLToOriginal = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Discovered relationships from TMSCHEMA_RELATIONSHIPS.
        /// Each entry: (FromTable, FromColumn, FromCardinality, ToTable, ToColumn, ToCardinality, IsActive).
        /// Cardinality: 1=One, 2=Many.
        /// </summary>
        private readonly List<SemanticModelRelationship> _discoveredRelationships = new();

        /// <summary>
        /// Maps table source name → entity name for quick lookup.
        /// </summary>
        private readonly Dictionary<string, string> _tableToEntityName = new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        /// <inheritdoc />
        public Dictionary<string, string> GraphQLStoredProcedureExposedNameToEntityNameMap { get; set; } = new();

        /// <inheritdoc />
        public Dictionary<RelationShipPair, ForeignKeyDefinition>? PairToFkDefinition { get; set; } = new();

        /// <inheritdoc />
        public Dictionary<EntityRelationshipKey, ForeignKeyDefinition> RelationshipToFkDefinition { get; set; } = new();

        /// <inheritdoc />
        public List<Exception> SqlMetadataExceptions { get; } = new();

        public SemanticModelMetadataProvider(RuntimeConfigProvider runtimeConfigProvider, ILogger<ISqlMetadataProvider>? logger = null)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _logger = logger;
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
            _runtimeConfigEntities = new RuntimeEntities(runtimeConfig.Entities.Entities);
            _isDevelopmentMode = runtimeConfig.IsDevelopmentMode();
            _databaseType = runtimeConfig.DataSource.DatabaseType;
            _connectionString = runtimeConfig.DataSource.ConnectionString;

            // Build the GraphQL type name to entity name map.
            foreach ((string entityName, Entity entity) in _runtimeConfigEntities)
            {
                if (entity.GraphQL is not null)
                {
                    string singularType = entity.GraphQL.Singular;
                    if (!string.IsNullOrEmpty(singularType))
                    {
                        _graphQLTypeToEntityNameMap.TryAdd(singularType, entityName);
                    }

                    string pluralType = entity.GraphQL.Plural;
                    if (!string.IsNullOrEmpty(pluralType))
                    {
                        _graphQLTypeToEntityNameMap.TryAdd(pluralType, entityName);
                    }
                }

                _graphQLTypeToEntityNameMap.TryAdd(entityName, entityName);
            }
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();

            // Discover column and measure metadata from the semantic model via ADOMD.NET.
            (Dictionary<string, List<(string ColumnName, int DataType, bool IsNullable)>> tableColumns,
             Dictionary<long, string> tableIdToName) = await DiscoverColumnsAsync();
            await DiscoverMeasuresAsync(tableIdToName);
            await DiscoverRelationshipsAsync(tableIdToName);

            // Build table→entity lookup (used for relationship auto-wiring).
            foreach ((string entityName, Entity entity) in _runtimeConfigEntities)
            {
                _tableToEntityName[entity.Source.Object] = entityName;
            }

            foreach ((string entityName, Entity entity) in _runtimeConfigEntities)
            {
                string sourceName = entity.Source.Object;
                SourceDefinition sourceDefinition = new();

                // Populate columns from discovered metadata.
                if (tableColumns.TryGetValue(sourceName, out var columns))
                {
                    foreach ((string columnName, int dataType, bool isNullable) in columns)
                    {
                        sourceDefinition.Columns[columnName] = new ColumnDefinition
                        {
                            SystemType = MapTomDataTypeToSystemType(dataType),
                            IsNullable = isNullable,
                            IsReadOnly = true
                        };
                    }
                }

                // Resolve which measures this entity exposes.
                HashSet<string> entityMeasureNames = ResolveMeasuresForEntity(entity);
                HashSet<string> registeredMeasures = new(StringComparer.OrdinalIgnoreCase);

                // Add measures as virtual columns on the entity's SourceDefinition.
                // Measures are always nullable (may return BLANK() in some row contexts),
                // read-only, and marked as IsMeasure for GraphQL directive application.
                // Column wins if a column and measure share the same name.
                // Measure names are sanitized to valid GraphQL identifiers (e.g., "Margin %" → "MarginPercent").
                foreach (string measureName in entityMeasureNames)
                {
                    if (!_measureRegistry.TryGetValue(measureName, out MeasureDefinition? measure))
                    {
                        continue;
                    }

                    string graphQLName = SanitizeMeasureName(measureName);
                    if (string.IsNullOrEmpty(graphQLName) || sourceDefinition.Columns.ContainsKey(graphQLName))
                    {
                        _logger?.LogWarning(
                            "Measure '{MeasureName}' could not be exposed: sanitized name '{GraphQLName}' is empty or conflicts with an existing column.",
                            measureName, graphQLName);
                        continue;
                    }

                    sourceDefinition.Columns[graphQLName] = new ColumnDefinition
                    {
                        SystemType = measure.SystemType,
                        IsNullable = true,
                        IsReadOnly = true,
                        IsMeasure = true
                    };
                    registeredMeasures.Add(graphQLName);

                    // Track mapping from sanitized → original for DAX query generation.
                    if (!string.Equals(graphQLName, measureName, StringComparison.Ordinal))
                    {
                        _measureGraphQLToOriginal[graphQLName] = measureName;
                        _logger?.LogInformation(
                            "Measure '{OriginalName}' exposed as '{GraphQLName}' (sanitized for GraphQL).",
                            measureName, graphQLName);
                    }
                }

                _entityMeasures[entityName] = registeredMeasures;

                DatabaseTable databaseTable = new(schemaName: string.Empty, tableName: sourceName)
                {
                    SourceType = EntitySourceType.Table,
                    TableDefinition = sourceDefinition
                };

                EntityToDatabaseObject[entityName] = databaseTable;

                // Build identity field mapping (exposed name == backing name for semantic models).
                Dictionary<string, string> fieldMap = new(StringComparer.OrdinalIgnoreCase);
                foreach (string col in sourceDefinition.Columns.Keys)
                {
                    fieldMap[col] = col;
                }

                _entityToFieldMappings[entityName] = fieldMap;

                // Register entity REST path mapping so REST routing can resolve entities.
                string path = GetEntityPath(entity, entityName).TrimStart('/');
                if (!string.IsNullOrEmpty(path))
                {
                    runtimeConfig.TryAddEntityPathNameToEntityName(path, entityName);
                }

                // Register entity-to-datasource mapping.
                runtimeConfig.TryAddEntityNameToDataSourceName(entityName);
            }

            // Wire relationships from entity config and log discovered model relationships.
            WireConfiguredRelationships();

            // Build the OData EDM model so $filter and $orderby parsing works.
            _odataParser = new ODataParser();
            _odataParser.BuildModel(this);
        }

        /// <summary>
        /// Wires relationships that are explicitly configured in the entity config.
        /// Also logs discovered model relationships to help users configure them.
        /// </summary>
        private void WireConfiguredRelationships()
        {
            // Log discovered relationships for user reference.
            if (_discoveredRelationships.Count > 0)
            {
                _logger?.LogInformation("Discovered {Count} relationships in semantic model.", _discoveredRelationships.Count);
                foreach (SemanticModelRelationship rel in _discoveredRelationships)
                {
                    if (!rel.IsActive)
                    {
                        continue;
                    }

                    string fromEntity = _tableToEntityName.GetValueOrDefault(rel.FromTable, rel.FromTable);
                    string toEntity = _tableToEntityName.GetValueOrDefault(rel.ToTable, rel.ToTable);
                    string fromCard = rel.FromCardinality == 1 ? "One" : "Many";
                    string toCard = rel.ToCardinality == 1 ? "One" : "Many";
                    _logger?.LogDebug(
                        "  {FromEntity}.{FromColumn} ({FromCard}) → {ToEntity}.{ToColumn} ({ToCard})",
                        fromEntity, rel.FromColumn, fromCard, toEntity, rel.ToColumn, toCard);
                }
            }

            // Wire explicitly configured relationships from entity config.
            foreach ((string entityName, Entity entity) in _runtimeConfigEntities)
            {
                if (entity.Relationships is null || entity.Relationships.Count == 0)
                {
                    continue;
                }

                if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? sourceDbObj) ||
                    sourceDbObj is not DatabaseTable sourceTable ||
                    sourceTable.TableDefinition is null)
                {
                    continue;
                }

                SourceDefinition sourceDef = sourceTable.TableDefinition;
                if (!sourceDef.SourceEntityRelationshipMap.ContainsKey(entityName))
                {
                    sourceDef.SourceEntityRelationshipMap[entityName] = new RelationshipMetadata();
                }

                RelationshipMetadata relMeta = sourceDef.SourceEntityRelationshipMap[entityName];

                foreach ((string relationshipName, EntityRelationship relationship) in entity.Relationships)
                {
                    string targetEntityName = relationship.TargetEntity;

                    if (!EntityToDatabaseObject.TryGetValue(targetEntityName, out DatabaseObject? targetDbObj) ||
                        targetDbObj is not DatabaseTable targetTable)
                    {
                        _logger?.LogWarning(
                            "Relationship '{RelName}' on entity '{Entity}' targets '{Target}' which is not a configured entity. Skipping.",
                            relationshipName, entityName, targetEntityName);
                        continue;
                    }

                    if (!relMeta.TargetEntityToFkDefinitionMap.ContainsKey(targetEntityName))
                    {
                        relMeta.TargetEntityToFkDefinitionMap[targetEntityName] = new List<ForeignKeyDefinition>();
                    }

                    ForeignKeyDefinition fkDef = new()
                    {
                        SourceEntityName = entityName,
                        Pair = new RelationShipPair
                        {
                            ReferencingDbTable = sourceTable,
                            ReferencedDbTable = targetTable
                        },
                        ReferencingEntityRole = RelationshipRole.Source,
                        ReferencedEntityRole = RelationshipRole.Target,
                        RelationshipName = relationshipName
                    };

                    if (relationship.SourceFields is not null)
                    {
                        fkDef.ReferencingColumns.AddRange(relationship.SourceFields);
                    }

                    if (relationship.TargetFields is not null)
                    {
                        fkDef.ReferencedColumns.AddRange(relationship.TargetFields);
                    }

                    relMeta.TargetEntityToFkDefinitionMap[targetEntityName].Add(fkDef);

                    EntityRelationshipKey key = new(entityName, relationshipName);
                    RelationshipToFkDefinition.TryAdd(key, fkDef);
                }
            }
        }

        /// <summary>
        /// Resolves the set of measure names to expose on a given entity based on its config.
        /// - null/empty: no measures
        /// - ["*"]: all non-hidden measures from the model
        /// - ["Sales", "Units"]: only the named measures
        /// </summary>
        private HashSet<string> ResolveMeasuresForEntity(Entity entity)
        {
            HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);

            if (entity.Measures is null || entity.Measures.Length == 0)
            {
                return result;
            }

            if (entity.Measures.Length == 1 && entity.Measures[0] == "*")
            {
                // Wildcard: expose all non-hidden measures.
                foreach (MeasureDefinition measure in _measureRegistry.Values)
                {
                    if (!measure.IsHidden)
                    {
                        result.Add(measure.Name);
                    }
                }
            }
            else
            {
                // Explicit list: expose only the named measures (if they exist).
                foreach (string name in entity.Measures)
                {
                    if (_measureRegistry.ContainsKey(name))
                    {
                        result.Add(name);
                    }
                    else
                    {
                        _logger?.LogWarning("Measure '{MeasureName}' specified in entity config was not found in the semantic model.", name);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the set of measure names exposed on the given entity.
        /// Used by the query engine to determine which selected fields are measures vs columns.
        /// Contains sanitized GraphQL-safe names.
        /// </summary>
        public HashSet<string> GetEntityMeasures(string entityName)
        {
            if (_entityMeasures.TryGetValue(entityName, out HashSet<string>? measures))
            {
                return measures;
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns the original semantic model measure name for a sanitized GraphQL field name.
        /// If the GraphQL name was not sanitized (i.e., original name was already valid), returns the input unchanged.
        /// Used by the query engine to build DAX measure references like [Margin %] from "MarginPercent".
        /// </summary>
        public string GetMeasureOriginalName(string graphQLName)
        {
            if (_measureGraphQLToOriginal.TryGetValue(graphQLName, out string? original))
            {
                return original;
            }

            return graphQLName;
        }

        /// <summary>
        /// Returns the model-wide measure registry.
        /// </summary>
        public IReadOnlyDictionary<string, MeasureDefinition> MeasureRegistry => _measureRegistry;

        /// <summary>
        /// Discovers column metadata from the semantic model using TMSCHEMA_TABLES and TMSCHEMA_COLUMNS DMVs.
        /// TMSCHEMA_COLUMNS provides the actual DAX data types (Int64, String, Double, etc.),
        /// unlike DBSCHEMA_COLUMNS which reports all columns as WSTR in Power BI Desktop.
        /// Returns a tuple of:
        ///   - Dictionary mapping table name → list of (ColumnName, TomDataType, IsNullable)
        ///   - Dictionary mapping table ID → table name (for all tables in the model, used by measure discovery)
        /// </summary>
        private async Task<(Dictionary<string, List<(string ColumnName, int DataType, bool IsNullable)>>,
                            Dictionary<long, string>)> DiscoverColumnsAsync()
        {
            Dictionary<string, List<(string, int, bool)>> result = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<long, string> allTableIdToName = new();

            // Collect all table source names from entities.
            HashSet<string> tableNames = new(StringComparer.OrdinalIgnoreCase);
            foreach ((_, Entity entity) in _runtimeConfigEntities)
            {
                tableNames.Add(entity.Source.Object);
            }

            try
            {
                using AdomdConnection connection = new(_connectionString);
                await Task.Run(() => connection.Open());

                // Step 1: Build TableID → TableName map from TMSCHEMA_TABLES (all tables for measure resolution).
                using (AdomdCommand tablesCmd = connection.CreateCommand())
                {
                    tablesCmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_TABLES";
                    using AdomdDataReader tablesReader = tablesCmd.ExecuteReader();

                    // Find column ordinals by name since positions may vary.
                    int idOrd = -1, nameOrd = -1;
                    for (int i = 0; i < tablesReader.FieldCount; i++)
                    {
                        string fn = tablesReader.GetName(i);
                        if (fn.Equals("ID", StringComparison.OrdinalIgnoreCase))
                        {
                            idOrd = i;
                        }
                        else if (fn.Equals("Name", StringComparison.OrdinalIgnoreCase))
                        {
                            nameOrd = i;
                        }
                    }

                    if (idOrd >= 0 && nameOrd >= 0)
                    {
                        while (tablesReader.Read())
                        {
                            long tableId = Convert.ToInt64(tablesReader.GetValue(idOrd));
                            string name = tablesReader.GetValue(nameOrd)?.ToString() ?? string.Empty;
                            allTableIdToName[tableId] = name;
                        }
                    }
                }

                // Build the set of table IDs for configured entities (for column filtering).
                HashSet<long> configuredTableIds = new();
                foreach ((long id, string name) in allTableIdToName)
                {
                    if (tableNames.Contains(name))
                    {
                        configuredTableIds.Add(id);
                    }
                }

                // Step 2: Read all columns from TMSCHEMA_COLUMNS and correlate by TableID.
                Dictionary<long, List<(string, int, bool)>> tableIdToColumns = new();
                using (AdomdCommand colsCmd = connection.CreateCommand())
                {
                    colsCmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_COLUMNS";
                    using AdomdDataReader colsReader = colsCmd.ExecuteReader();

                    int tableIdOrd = -1, explicitNameOrd = -1, inferredNameOrd = -1;
                    int explicitDataTypeOrd = -1, isNullableOrd = -1, isHiddenOrd = -1, typeOrd = -1;
                    for (int i = 0; i < colsReader.FieldCount; i++)
                    {
                        string fn = colsReader.GetName(i);
                        if (fn.Equals("TableID", StringComparison.OrdinalIgnoreCase))
                        {
                            tableIdOrd = i;
                        }
                        else if (fn.Equals("ExplicitName", StringComparison.OrdinalIgnoreCase))
                        {
                            explicitNameOrd = i;
                        }
                        else if (fn.Equals("InferredName", StringComparison.OrdinalIgnoreCase))
                        {
                            inferredNameOrd = i;
                        }
                        else if (fn.Equals("ExplicitDataType", StringComparison.OrdinalIgnoreCase))
                        {
                            explicitDataTypeOrd = i;
                        }
                        else if (fn.Equals("IsNullable", StringComparison.OrdinalIgnoreCase))
                        {
                            isNullableOrd = i;
                        }
                        else if (fn.Equals("IsHidden", StringComparison.OrdinalIgnoreCase))
                        {
                            isHiddenOrd = i;
                        }
                        else if (fn.Equals("Type", StringComparison.OrdinalIgnoreCase))
                        {
                            typeOrd = i;
                        }
                    }

                    while (colsReader.Read())
                    {
                        long tableId = Convert.ToInt64(colsReader.GetValue(tableIdOrd));
                        if (!configuredTableIds.Contains(tableId))
                        {
                            continue;
                        }

                        // Skip hidden columns and RowNumber columns (Type=2 are calculated, Type=1 are data columns).
                        if (isHiddenOrd >= 0 && !colsReader.IsDBNull(isHiddenOrd) && Convert.ToBoolean(colsReader.GetValue(isHiddenOrd)))
                        {
                            continue;
                        }

                        // Column type: 1=Data, 2=Calculated, 3=RowNumber
                        if (typeOrd >= 0 && !colsReader.IsDBNull(typeOrd))
                        {
                            int colType = Convert.ToInt32(colsReader.GetValue(typeOrd));
                            if (colType == 3) // RowNumber
                            {
                                continue;
                            }
                        }

                        string columnName = explicitNameOrd >= 0 && !colsReader.IsDBNull(explicitNameOrd)
                            ? colsReader.GetValue(explicitNameOrd)?.ToString() ?? string.Empty
                            : (inferredNameOrd >= 0 ? colsReader.GetValue(inferredNameOrd)?.ToString() ?? string.Empty : string.Empty);

                        if (string.IsNullOrEmpty(columnName) || columnName.StartsWith("RowNumber", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        int dataType = explicitDataTypeOrd >= 0 && !colsReader.IsDBNull(explicitDataTypeOrd)
                            ? Convert.ToInt32(colsReader.GetValue(explicitDataTypeOrd))
                            : 2; // Default to String

                        bool isNullable = isNullableOrd >= 0 && !colsReader.IsDBNull(isNullableOrd)
                            && Convert.ToBoolean(colsReader.GetValue(isNullableOrd));

                        if (!tableIdToColumns.TryGetValue(tableId, out var colList))
                        {
                            colList = new List<(string, int, bool)>();
                            tableIdToColumns[tableId] = colList;
                        }

                        colList.Add((columnName, dataType, isNullable));
                    }
                }

                // Step 3: Map TableID columns to table names.
                foreach ((long tableId, string tableName) in allTableIdToName)
                {
                    if (configuredTableIds.Contains(tableId) && tableIdToColumns.TryGetValue(tableId, out var columns))
                    {
                        result[tableName] = columns;
                    }
                    else if (configuredTableIds.Contains(tableId))
                    {
                        result[tableName] = new List<(string, int, bool)>();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to discover column metadata from semantic model. Entities will have no column definitions.");
            }

            return (result, allTableIdToName);
        }

        /// <summary>
        /// Discovers all measures from the semantic model via TMSCHEMA_MEASURES DMV.
        /// Populates the model-wide _measureRegistry with MeasureDefinition records.
        /// </summary>
        /// <param name="tableIdToName">Table ID → name map from column discovery (covers all tables in the model).</param>
        private async Task DiscoverMeasuresAsync(Dictionary<long, string> tableIdToName)
        {
            if (tableIdToName.Count == 0)
            {
                return;
            }

            try
            {
                using AdomdConnection connection = new(_connectionString);
                await Task.Run(() => connection.Open());

                using AdomdCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_MEASURES";
                using AdomdDataReader reader = cmd.ExecuteReader();

                int nameOrd = -1, tableIdOrd = -1, dataTypeOrd = -1;
                int expressionOrd = -1, isHiddenOrd = -1;
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string fn = reader.GetName(i);
                    if (fn.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        nameOrd = i;
                    }
                    else if (fn.Equals("TableID", StringComparison.OrdinalIgnoreCase))
                    {
                        tableIdOrd = i;
                    }
                    else if (fn.Equals("DataType", StringComparison.OrdinalIgnoreCase))
                    {
                        dataTypeOrd = i;
                    }
                    else if (fn.Equals("Expression", StringComparison.OrdinalIgnoreCase))
                    {
                        expressionOrd = i;
                    }
                    else if (fn.Equals("IsHidden", StringComparison.OrdinalIgnoreCase))
                    {
                        isHiddenOrd = i;
                    }
                }

                if (nameOrd < 0)
                {
                    _logger?.LogWarning("TMSCHEMA_MEASURES did not contain expected columns. Measure discovery skipped.");
                    return;
                }

                while (reader.Read())
                {
                    string name = reader.GetValue(nameOrd)?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    long tableId = tableIdOrd >= 0 && !reader.IsDBNull(tableIdOrd)
                        ? Convert.ToInt64(reader.GetValue(tableIdOrd))
                        : -1;
                    string homeTable = tableIdToName.TryGetValue(tableId, out string? tableName)
                        ? tableName
                        : string.Empty;

                    int dataType = dataTypeOrd >= 0 && !reader.IsDBNull(dataTypeOrd)
                        ? Convert.ToInt32(reader.GetValue(dataTypeOrd))
                        : 2; // Default to String

                    string expression = expressionOrd >= 0 && !reader.IsDBNull(expressionOrd)
                        ? reader.GetValue(expressionOrd)?.ToString() ?? string.Empty
                        : string.Empty;

                    bool isHidden = isHiddenOrd >= 0 && !reader.IsDBNull(isHiddenOrd)
                        && Convert.ToBoolean(reader.GetValue(isHiddenOrd));

                    MeasureDefinition measure = new(
                        Name: name,
                        Expression: expression,
                        SystemType: MapTomDataTypeToSystemType(dataType),
                        HomeTable: homeTable,
                        IsHidden: isHidden);

                    _measureRegistry[name] = measure;
                }

                _logger?.LogInformation("Discovered {Count} measures from semantic model.", _measureRegistry.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to discover measures from semantic model. Measure support will be unavailable.");
            }
        }

        /// <summary>
        /// Discovers relationships from the semantic model via TMSCHEMA_RELATIONSHIPS DMV.
        /// Also builds a ColumnID → ColumnName map via TMSCHEMA_COLUMNS for resolving FK columns.
        /// Populates _discoveredRelationships list.
        /// </summary>
        /// <param name="tableIdToName">Table ID → name map from column discovery.</param>
        private async Task DiscoverRelationshipsAsync(Dictionary<long, string> tableIdToName)
        {
            if (tableIdToName.Count == 0)
            {
                return;
            }

            try
            {
                using AdomdConnection connection = new(_connectionString);
                await Task.Run(() => connection.Open());

                // Build ColumnID → (TableID, ColumnName) map for resolving relationship endpoints.
                Dictionary<long, (long TableID, string Name)> columnIdToInfo = new();
                using (AdomdCommand colCmd = connection.CreateCommand())
                {
                    colCmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_COLUMNS";
                    using AdomdDataReader colReader = colCmd.ExecuteReader();

                    int idOrd = -1, tableIdOrd = -1, nameOrd = -1, infNameOrd = -1;
                    for (int i = 0; i < colReader.FieldCount; i++)
                    {
                        string fn = colReader.GetName(i);
                        if (fn.Equals("ID", StringComparison.OrdinalIgnoreCase))
                        {
                            idOrd = i;
                        }
                        else if (fn.Equals("TableID", StringComparison.OrdinalIgnoreCase))
                        {
                            tableIdOrd = i;
                        }
                        else if (fn.Equals("ExplicitName", StringComparison.OrdinalIgnoreCase))
                        {
                            nameOrd = i;
                        }
                        else if (fn.Equals("InferredName", StringComparison.OrdinalIgnoreCase))
                        {
                            infNameOrd = i;
                        }
                    }

                    while (colReader.Read())
                    {
                        long colId = Convert.ToInt64(colReader.GetValue(idOrd));
                        long tblId = Convert.ToInt64(colReader.GetValue(tableIdOrd));
                        string name = nameOrd >= 0 && !colReader.IsDBNull(nameOrd)
                            ? colReader.GetValue(nameOrd)?.ToString() ?? string.Empty
                            : (infNameOrd >= 0 ? colReader.GetValue(infNameOrd)?.ToString() ?? string.Empty : string.Empty);
                        columnIdToInfo[colId] = (tblId, name);
                    }
                }

                // Read relationships from TMSCHEMA_RELATIONSHIPS.
                using AdomdCommand relCmd = connection.CreateCommand();
                relCmd.CommandText = "SELECT * FROM $SYSTEM.TMSCHEMA_RELATIONSHIPS";
                using AdomdDataReader relReader = relCmd.ExecuteReader();

                int fromTableIdOrd = -1, fromColumnIdOrd = -1, fromCardOrd = -1;
                int toTableIdOrd = -1, toColumnIdOrd = -1, toCardOrd = -1;
                int isActiveOrd = -1, crossFilterOrd = -1;

                for (int i = 0; i < relReader.FieldCount; i++)
                {
                    string fn = relReader.GetName(i);
                    if (fn.Equals("FromTableID", StringComparison.OrdinalIgnoreCase))
                    {
                        fromTableIdOrd = i;
                    }
                    else if (fn.Equals("FromColumnID", StringComparison.OrdinalIgnoreCase))
                    {
                        fromColumnIdOrd = i;
                    }
                    else if (fn.Equals("FromCardinality", StringComparison.OrdinalIgnoreCase))
                    {
                        fromCardOrd = i;
                    }
                    else if (fn.Equals("ToTableID", StringComparison.OrdinalIgnoreCase))
                    {
                        toTableIdOrd = i;
                    }
                    else if (fn.Equals("ToColumnID", StringComparison.OrdinalIgnoreCase))
                    {
                        toColumnIdOrd = i;
                    }
                    else if (fn.Equals("ToCardinality", StringComparison.OrdinalIgnoreCase))
                    {
                        toCardOrd = i;
                    }
                    else if (fn.Equals("IsActive", StringComparison.OrdinalIgnoreCase))
                    {
                        isActiveOrd = i;
                    }
                    else if (fn.Equals("CrossFilteringBehavior", StringComparison.OrdinalIgnoreCase))
                    {
                        crossFilterOrd = i;
                    }
                }

                while (relReader.Read())
                {
                    long fromTableId = Convert.ToInt64(relReader.GetValue(fromTableIdOrd));
                    long fromColumnId = Convert.ToInt64(relReader.GetValue(fromColumnIdOrd));
                    int fromCard = Convert.ToInt32(relReader.GetValue(fromCardOrd));
                    long toTableId = Convert.ToInt64(relReader.GetValue(toTableIdOrd));
                    long toColumnId = Convert.ToInt64(relReader.GetValue(toColumnIdOrd));
                    int toCard = Convert.ToInt32(relReader.GetValue(toCardOrd));
                    bool isActive = isActiveOrd >= 0 && !relReader.IsDBNull(isActiveOrd)
                        && Convert.ToBoolean(relReader.GetValue(isActiveOrd));
                    int crossFilter = crossFilterOrd >= 0 && !relReader.IsDBNull(crossFilterOrd)
                        ? Convert.ToInt32(relReader.GetValue(crossFilterOrd))
                        : 1;

                    // Resolve names from IDs.
                    string fromTable = tableIdToName.GetValueOrDefault(fromTableId, $"TableID:{fromTableId}");
                    string toTable = tableIdToName.GetValueOrDefault(toTableId, $"TableID:{toTableId}");
                    string fromColumn = columnIdToInfo.TryGetValue(fromColumnId, out var fromColInfo)
                        ? fromColInfo.Name : $"ColumnID:{fromColumnId}";
                    string toColumn = columnIdToInfo.TryGetValue(toColumnId, out var toColInfo)
                        ? toColInfo.Name : $"ColumnID:{toColumnId}";

                    _discoveredRelationships.Add(new SemanticModelRelationship(
                        FromTable: fromTable,
                        FromColumn: fromColumn,
                        FromCardinality: fromCard,
                        ToTable: toTable,
                        ToColumn: toColumn,
                        ToCardinality: toCard,
                        IsActive: isActive,
                        CrossFilteringBehavior: crossFilter));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to discover relationships from semantic model.");
            }
        }

        /// <summary>
        /// Maps TOM (Tabular Object Model) DataType codes to .NET System types.
        /// These are the ExplicitDataType values from TMSCHEMA_COLUMNS.
        /// </summary>
        private static Type MapTomDataTypeToSystemType(int tomDataType)
        {
            return tomDataType switch
            {
                2 => typeof(string),          // String
                6 => typeof(long),            // Int64 / WholeNumber
                8 => typeof(double),          // Double
                9 => typeof(DateTime),        // DateTime
                10 => typeof(decimal),        // Decimal
                11 => typeof(bool),           // Boolean
                17 => typeof(byte[]),         // Binary
                _ => typeof(string),          // Default to string for unknown types
            };
        }

        /// <summary>
        /// Checks whether a name is a valid GraphQL identifier: /^[_A-Za-z][_0-9A-Za-z]*$/.
        /// </summary>
        private static bool IsValidGraphQLName(string name)
        {
            return !string.IsNullOrEmpty(name) && Regex.IsMatch(name, @"^[_A-Za-z][_0-9A-Za-z]*$");
        }

        /// <summary>
        /// Symbol-to-word replacements for measure name sanitization.
        /// Symbols are replaced with their short lowercase word equivalents.
        /// </summary>
        private static readonly Dictionary<string, string> s_symbolReplacements = new()
        {
            { "%", "pct" },
            { "$", "usd" },
            { "#", "num" },
            { "&", "and" },
            { "@", "at" },
            { "+", "plus" },
            { "=", "eq" },
        };

        /// <summary>
        /// Sanitizes a measure name to a valid GraphQL identifier using predictable rules:
        /// 1. Known symbols → short word (lowercase): % → pct, $ → usd, # → num, &amp; → and, @ → at
        /// 2. All remaining non-alphanumeric chars → underscore
        /// 3. Collapse consecutive underscores to single underscore
        /// 4. Trim leading/trailing underscores
        /// 5. If starts with digit, prefix with underscore
        ///
        /// Examples: "Margin %" → "Margin_pct", "Labor and Overhead Cost" → "Labor_and_Overhead_Cost"
        /// </summary>
        private static string SanitizeMeasureName(string originalName)
        {
            if (IsValidGraphQLName(originalName))
            {
                return originalName;
            }

            string working = originalName;

            // Step 1: Replace known symbols with short words.
            foreach ((string symbol, string word) in s_symbolReplacements)
            {
                working = working.Replace(symbol, word);
            }

            // Step 2: Replace all remaining non-alphanumeric chars with underscore.
            working = Regex.Replace(working, @"[^a-zA-Z0-9]", "_");

            // Step 3: Collapse consecutive underscores.
            working = Regex.Replace(working, @"_+", "_");

            // Step 4: Trim leading/trailing underscores.
            working = working.Trim('_');

            // Step 5: Ensure starts with letter or underscore.
            if (working.Length == 0)
            {
                return string.Empty;
            }

            if (char.IsDigit(working[0]))
            {
                working = "_" + working;
            }

            return working;
        }

        /// <summary>
        /// Returns the REST path for the entity. Uses the custom path if configured,
        /// otherwise uses the entity name.
        /// </summary>
        private static string GetEntityPath(Entity entity, string entityName)
        {
            if (entity.Rest is null || (entity.Rest.Enabled && string.IsNullOrEmpty(entity.Rest.Path)))
            {
                return entityName;
            }

            if (!entity.Rest.Enabled)
            {
                return string.Empty;
            }

            return entity.Rest.Path!;
        }

        /// <inheritdoc />
        public void InitializeAsync(
            Dictionary<string, DatabaseObject> entityToDatabaseObject,
            Dictionary<string, string> graphQLStoredProcedureExposedNameToEntityNameMap)
        {
            EntityToDatabaseObject = entityToDatabaseObject;
            GraphQLStoredProcedureExposedNameToEntityNameMap = graphQLStoredProcedureExposedNameToEntityNameMap;
        }

        /// <inheritdoc />
        public DatabaseType GetDatabaseType()
        {
            return _databaseType;
        }

        /// <inheritdoc />
        public string GetDefaultSchemaName()
        {
            return string.Empty;
        }

        /// <inheritdoc />
        public string GetSchemaName(string entityName)
        {
            return string.Empty;
        }

        /// <inheritdoc />
        public string GetDatabaseObjectName(string entityName)
        {
            Entity entity = _runtimeConfigEntities[entityName];
            return entity.Source.Object;
        }

        /// <inheritdoc />
        public (string, string) ParseSchemaAndDbTableName(string source)
        {
            return (string.Empty, source);
        }

        /// <inheritdoc />
        public SourceDefinition GetSourceDefinition(string entityName)
        {
            if (EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) &&
                dbObject is DatabaseTable table &&
                table.TableDefinition is not null)
            {
                return table.TableDefinition;
            }

            return new SourceDefinition();
        }

        /// <inheritdoc />
        public StoredProcedureDefinition GetStoredProcedureDefinition(string entityName)
        {
            throw new NotSupportedException("Stored procedures are not supported for Semantic Model backends.");
        }

        /// <inheritdoc />
        public bool VerifyForeignKeyExistsInDB(DatabaseTable databaseObjectA, DatabaseTable databaseObjectB)
        {
            return false;
        }

        /// <inheritdoc />
        public List<string> GetSchemaGraphQLFieldNamesForEntityName(string entityName)
        {
            if (_entityToFieldMappings.TryGetValue(entityName, out Dictionary<string, string>? fields))
            {
                return fields.Keys.ToList();
            }

            return new List<string>();
        }

        /// <inheritdoc />
        public string? GetSchemaGraphQLFieldTypeFromFieldName(string entityName, string fieldName)
        {
            if (EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) &&
                dbObject is DatabaseTable table &&
                table.TableDefinition is not null &&
                table.TableDefinition.Columns.TryGetValue(fieldName, out ColumnDefinition? columnDef))
            {
                return Azure.DataApiBuilder.Service.GraphQLBuilder.Sql.SchemaConverter.GetGraphQLTypeFromSystemType(columnDef.SystemType);
            }

            return null;
        }

        /// <inheritdoc />
        public FieldDefinitionNode? GetSchemaGraphQLFieldFromFieldName(string entityName, string fieldName)
        {
            return null;
        }

        /// <inheritdoc />
        public ODataParser GetODataParser()
        {
            return _odataParser ?? throw new InvalidOperationException(
                "OData parser has not been initialized. Ensure InitializeAsync() has been called.");
        }

        /// <inheritdoc />
        public IQueryBuilder GetQueryBuilder()
        {
            throw new NotSupportedException("SQL query builder is not used for Semantic Model backends. DAX queries are built directly by the engine.");
        }

        /// <inheritdoc />
        public bool TryGetExposedColumnName(string entityName, string backingFieldName, [NotNullWhen(true)] out string? name)
        {
            name = backingFieldName;
            return true;
        }

        /// <inheritdoc />
        public bool TryGetBackingColumn(string entityName, string field, [NotNullWhen(true)] out string? name)
        {
            name = field;
            return true;
        }

        /// <inheritdoc />
        public bool TryGetExposedFieldToBackingFieldMap(string entityName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? mappings)
        {
            if (_entityToFieldMappings.TryGetValue(entityName, out Dictionary<string, string>? fieldMap))
            {
                mappings = fieldMap;
                return true;
            }

            mappings = new Dictionary<string, string>();
            return true;
        }

        /// <inheritdoc />
        public bool TryGetBackingFieldToExposedFieldMap(string entityName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? mappings)
        {
            if (_entityToFieldMappings.TryGetValue(entityName, out Dictionary<string, string>? fieldMap))
            {
                mappings = fieldMap;
                return true;
            }

            mappings = new Dictionary<string, string>();
            return true;
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, DatabaseObject> GetEntityNamesAndDbObjects()
        {
            return EntityToDatabaseObject;
        }

        /// <inheritdoc />
        public string? GetPartitionKeyPath(string database, string container)
        {
            throw new NotSupportedException("Partition key paths are not applicable to Semantic Model backends.");
        }

        /// <inheritdoc />
        public void SetPartitionKeyPath(string database, string container, string partitionKeyPath)
        {
            throw new NotSupportedException("Partition key paths are not applicable to Semantic Model backends.");
        }

        /// <inheritdoc />
        public string GetEntityName(string graphQLType)
        {
            if (_runtimeConfigEntities.ContainsKey(graphQLType))
            {
                return graphQLType;
            }

            if (_graphQLTypeToEntityNameMap.TryGetValue(graphQLType, out string? entityName))
            {
                return entityName;
            }

            throw new System.Collections.Generic.KeyNotFoundException($"GraphQL type '{graphQLType}' does not match any entity name in the runtime config.");
        }

        /// <inheritdoc />
        public bool IsDevelopmentMode()
        {
            return _isDevelopmentMode;
        }
    }
}

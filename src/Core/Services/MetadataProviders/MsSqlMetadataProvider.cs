// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// MsSQL specific override for SqlMetadataProvider.
    /// All the method definitions from base class are sufficient
    /// this class is only created for symmetricity with MySql
    /// and ease of expanding the generics specific to MsSql.
    /// </summary>
    public class MsSqlMetadataProvider :
        SqlMetadataProvider<SqlConnection, SqlDataAdapter, SqlCommand>
    {
        private RuntimeConfigProvider _runtimeConfigProvider;

        public MsSqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            RuntimeConfigValidator runtimeConfigValidator,
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
            : base(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory, logger, dataSourceName, isValidateOnly)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// SQL Server CLR user-defined types. Microsoft.Data.SqlClient resolves their CLR type
        /// through the Microsoft.SqlServer.Types assembly, which Data API builder does not
        /// reference, so the reader reports no type for the column and the data adapter fails.
        /// Deliberately limited to the types that cannot be read at all: timestamp, xml and vector
        /// columns do resolve to a CLR type and are left untouched.
        /// </summary>
        private static readonly ImmutableHashSet<string> _unsupportedColumnDataTypes =
            ImmutableHashSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "geometry",
                "geography",
                "hierarchyid");

        /// <inheritdoc/>
        protected override ImmutableHashSet<string> UnsupportedColumnDataTypes => _unsupportedColumnDataTypes;

        /// <inheritdoc/>
        protected override async Task<ObjectCatalogMetadata?> GetObjectCatalogMetadataAsync(
            string schemaName,
            string tableName)
        {
            string schemaParamName = $"{BaseQueryStructure.PARAM_NAME_PREFIX}param0";
            string tableParamName = $"{BaseQueryStructure.PARAM_NAME_PREFIX}param1";

            // The object is resolved through object_id(). Both name parts go through QUOTENAME:
            // object_id() parses its argument as a multi-part name, so an unquoted schema or table
            // holding a dot, a space, a reserved word or a closing bracket resolves to the wrong
            // object or to null — and a null object_id returns no rows, which would leave the
            // projection at "SELECT *" and bring #3801 back for that object. QUOTENAME also doubles
            // an embedded "]", so the names are passed raw and quoted by the server.
            // is_hidden marks the period columns of a temporal table declared
            // GENERATED ALWAYS ... HIDDEN, which "SELECT *" does not return. key_ordinal is null for
            // every column outside the primary key, and 1-based within it; an index's included
            // columns report 0 and are not part of the key.
            // Unique indexes are returned alongside the primary key, because absent a primary key
            // the data adapter reports a non-nullable unique key as DataTable.PrimaryKey on the
            // unnarrowed path, and the narrowed path has to do the same. Filtered and disabled
            // indexes do not identify every row, and an index's included columns report key_ordinal
            // 0 and are not part of its key.
            string query =
                "select c.name as COLUMN_NAME, c.is_hidden as IS_HIDDEN, c.is_identity as IS_IDENTITY, "
                + "c.is_nullable as IS_NULLABLE, i.index_id as INDEX_ID, "
                + "i.is_primary_key as IS_PRIMARY_KEY, ic.key_ordinal as KEY_ORDINAL "
                + "from sys.columns as c "
                + "left join sys.index_columns as ic on ic.object_id = c.object_id "
                + "and ic.column_id = c.column_id and ic.key_ordinal > 0 "
                + "left join sys.indexes as i on i.object_id = ic.object_id and i.index_id = ic.index_id "
                + "and i.is_unique = 1 and i.is_disabled = 0 and i.has_filter = 0 "
                + $"where c.object_id = object_id(quotename({schemaParamName})+'.'+quotename({tableParamName}));";

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { schemaParamName, new(schemaName, DbType.String) },
                { tableParamName, new(tableName, DbType.String) }
            };

            try
            {
                return await QueryExecutor.ExecuteQueryAsync(
                    sqltext: query,
                    parameters: parameters,
                    dataReaderHandler: SummarizeObjectCatalogMetadataAsync,
                    dataSourceName: _dataSourceName);
            }
            catch (Exception ex)
            {
                // sys.columns.is_hidden exists from SQL Server 2016 on. Where the catalog cannot
                // answer — a dedicated SQL pool, or a login without VIEW DEFINITION — returning null
                // leaves the projection at "*", which is exactly the behavior before this change.
                // Logged at Warning, not Debug: the fallback changes what the object exposes — the
                // projection stays "SELECT *" and an unsupported column takes the entity down with
                // the provider's own opaque error — so the reason has to be visible by default.
                _logger.LogWarning(
                    "Unable to read catalog metadata for {schemaName}.{tableName}: {message}",
                    schemaName,
                    tableName,
                    ex.Message);

                return null;
            }
        }

        /// <summary>
        /// Turns the catalog rows read by <see cref="GetObjectCatalogMetadataAsync"/> into
        /// <see cref="SqlMetadataProvider{ConnectionT, DataAdapterT, CommandT}.ObjectCatalogMetadata"/>.
        /// Returns null when the object has no rows: that means it was not found in the catalog, and
        /// claiming it has no hidden columns would be a guess.
        /// </summary>
        private async Task<ObjectCatalogMetadata?> SummarizeObjectCatalogMetadataAsync(
            DbDataReader reader,
            List<string>? args = null)
        {
            DbResultSet catalogRows = await QueryExecutor.ExtractResultSetFromDbDataReaderAsync(reader);

            if (catalogRows.Rows.Count == 0)
            {
                return null;
            }

            ObjectCatalogMetadata catalogMetadata = new();
            Dictionary<string, bool> nullabilityByColumn = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, CatalogIndexKey> indexKeysByIndexId = new();

            foreach (DbResultSetRow catalogRow in catalogRows.Rows)
            {
                Dictionary<string, object?> columnInfo = catalogRow.Columns;

                if (columnInfo["COLUMN_NAME"] is not string columnName)
                {
                    continue;
                }

                if (columnInfo["IS_HIDDEN"] is bool isHidden && isHidden)
                {
                    catalogMetadata.HiddenColumns.Add(columnName);
                }

                if (columnInfo["IS_IDENTITY"] is bool isIdentity && isIdentity)
                {
                    catalogMetadata.IdentityColumns.Add(columnName);
                }

                // A column the catalog does not describe as non-nullable is treated as nullable, so
                // an unreadable flag can only disqualify a unique key, never promote one.
                nullabilityByColumn[columnName] = columnInfo["IS_NULLABLE"] is not bool isNullable || isNullable;

                // A column outside every eligible unique index carries nulls for the index members.
                if (columnInfo["INDEX_ID"] is not int indexId
                    || columnInfo["KEY_ORDINAL"] is not byte keyOrdinal)
                {
                    continue;
                }

                if (!indexKeysByIndexId.TryGetValue(indexId, out CatalogIndexKey? indexKey))
                {
                    indexKey = new CatalogIndexKey
                    {
                        IsPrimaryKey = columnInfo["IS_PRIMARY_KEY"] is bool isPrimaryKey && isPrimaryKey
                    };
                    indexKeysByIndexId[indexId] = indexKey;
                }

                indexKey.Columns.Add((columnName, keyOrdinal));
            }

            // Index order is creation order, which makes the candidate choice deterministic.
            List<int> indexIds = new(indexKeysByIndexId.Keys);
            indexIds.Sort();

            foreach (int indexId in indexIds)
            {
                CatalogIndexKey indexKey = indexKeysByIndexId[indexId];
                indexKey.Columns.Sort((left, right) => left.KeyOrdinal.CompareTo(right.KeyOrdinal));

                List<string> keyColumns = new();
                bool holdsNoNull = true;

                foreach ((string ColumnName, byte KeyOrdinal) keyColumn in indexKey.Columns)
                {
                    keyColumns.Add(keyColumn.ColumnName);

                    if (nullabilityByColumn.TryGetValue(keyColumn.ColumnName, out bool isNullable) && isNullable)
                    {
                        holdsNoNull = false;
                    }
                }

                if (indexKey.IsPrimaryKey)
                {
                    catalogMetadata.PrimaryKeyColumns.AddRange(keyColumns);
                }
                else if (holdsNoNull)
                {
                    // Matches the data adapter, which promotes a unique key to the primary key only
                    // when none of its columns can hold a null.
                    catalogMetadata.UniqueKeyCandidates.Add(keyColumns);
                }
            }

            return catalogMetadata;
        }

        /// <inheritdoc/>
        protected override async Task<(List<string> Key, string? UnreachableKeyReason)> GetProjectionKeyFromResultSetAsync(
            string selectStatement)
        {
            List<ProjectionSourceColumn> projectionColumns = await DescribeProjectionAsync(selectStatement);

            if (projectionColumns.Count == 0)
            {
                return (new List<string>(), null);
            }

            // The key is taken from the underlying object's own catalog entry rather than from
            // is_part_of_unique_key, which is reported per column as the union of every unique key
            // of the result. That union cannot be split back into one key: a view over a table with
            // both a primary key and a unique index would yield the two fused into an
            // over-specified key, and a key member the projection left out comes back flagged
            // hidden, which would yield a partial key that does not identify a row at all.
            string? sourceSchema = null;
            string? sourceTable = null;
            Dictionary<string, string> projectionBySourceColumn = new(StringComparer.OrdinalIgnoreCase);

            foreach (ProjectionSourceColumn projectionColumn in projectionColumns)
            {
                if (projectionColumn.SourceSchema is null
                    || projectionColumn.SourceTable is null
                    || projectionColumn.SourceColumn is null)
                {
                    continue;
                }

                // Every row counts toward this check, the hidden ones included. Browse mode reports
                // the key columns of each participating object, so an object whose every selected
                // column was dropped from the projection survives in hidden rows alone. Skipping
                // those first would read a join as a single object and infer one side's key for a
                // result the other side can duplicate rows of.
                if (sourceSchema is null)
                {
                    sourceSchema = projectionColumn.SourceSchema;
                    sourceTable = projectionColumn.SourceTable;
                }
                else if (!string.Equals(sourceSchema, projectionColumn.SourceSchema, StringComparison.Ordinal)
                    || !string.Equals(sourceTable, projectionColumn.SourceTable, StringComparison.Ordinal))
                {
                    // More than one underlying object. Inferring a key across a join is out of
                    // scope; such an entity needs source.key-fields, as it did before this change.
                    return (new List<string>(), null);
                }

                // Only a column the projection selects can carry a key: a hidden one is exactly
                // what the projection left out.
                if (!projectionColumn.IsHidden)
                {
                    projectionBySourceColumn[projectionColumn.SourceColumn] = projectionColumn.Name;
                }
            }

            if (sourceSchema is null || sourceTable is null)
            {
                return (new List<string>(), null);
            }

            ObjectCatalogMetadata? sourceCatalogMetadata =
                await GetCachedObjectCatalogMetadataAsync(sourceSchema, sourceTable);

            if (sourceCatalogMetadata is null)
            {
                return (new List<string>(), null);
            }

            // The underlying object's primary key comes first, then its unique keys, in index order
            // — the preference the table path applies. Unlike a table, though, a view can simply
            // not select the primary key, which is ordinary design rather than a key it cannot
            // express, so a primary key that is out of reach does not end the search: a unique key
            // the projection does carry whole identifies a row just as well.
            List<List<string>> sourceKeys = new();

            if (sourceCatalogMetadata.PrimaryKeyColumns.Count > 0)
            {
                sourceKeys.Add(sourceCatalogMetadata.PrimaryKeyColumns);
            }

            sourceKeys.AddRange(sourceCatalogMetadata.UniqueKeyCandidates);

            string? unreachableKeyColumn = null;

            foreach (List<string> sourceKey in sourceKeys)
            {
                List<string> projectionKey = new();
                string? missingKeyColumn = null;

                foreach (string sourceKeyColumn in sourceKey)
                {
                    if (!projectionBySourceColumn.TryGetValue(sourceKeyColumn, out string? projectionColumnName))
                    {
                        missingKeyColumn = sourceKeyColumn;
                        break;
                    }

                    projectionKey.Add(projectionColumnName);
                }

                if (missingKeyColumn is null && projectionKey.Count > 0)
                {
                    return (projectionKey, null);
                }

                // Remembered from the first key that came closest, to name something concrete if no
                // key resolves at all. A partial key is never returned: it silently matches more
                // than one row on an update or a delete.
                unreachableKeyColumn ??= missingKeyColumn;
            }

            if (unreachableKeyColumn is not null)
            {
                // Returned rather than thrown: an object whose source.key-fields is configured is
                // reachable by that key, and rejecting it here would recommend exactly what was
                // already configured. The caller reports this only if the object ends up keyless.
                return (
                    new List<string>(),
                    $"No key of {sourceSchema}.{sourceTable}, which this object is built on, is fully exposed "
                        + $"by it: the nearest one includes the column {unreachableKeyColumn}, which this object does "
                        + "not expose. It therefore cannot be reached by key. Configure source.key-fields with columns "
                        + "it does expose that identify a row uniquely, if there are any.");
            }

            return (new List<string>(), null);
        }

        /// <summary>
        /// Describes a projection and returns what each of its columns resolves to in the catalog.
        /// Browse information is what populates the source_* columns, and it is also what resolves a
        /// view's columns to the object underneath. Metadata only: no row is read and no CLR type is
        /// requested, so an unsupported column elsewhere in the object cannot fail this.
        /// </summary>
        private async Task<List<ProjectionSourceColumn>> DescribeProjectionAsync(string selectStatement)
        {
            string statementParamName = $"{BaseQueryStructure.PARAM_NAME_PREFIX}param0";

            string query =
                "select r.name as COLUMN_NAME, r.source_schema as SOURCE_SCHEMA, "
                + "r.source_table as SOURCE_TABLE, r.source_column as SOURCE_COLUMN, "
                + "r.is_hidden as IS_HIDDEN "
                + $"from sys.dm_exec_describe_first_result_set({statementParamName}, null, 1) as r "
                + "order by r.column_ordinal;";

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { statementParamName, new(selectStatement, DbType.String) }
            };

            try
            {
                List<ProjectionSourceColumn>? projectionColumns = await QueryExecutor.ExecuteQueryAsync(
                    sqltext: query,
                    parameters: parameters,
                    dataReaderHandler: SummarizeProjectionSourceColumnsAsync,
                    dataSourceName: _dataSourceName);

                return projectionColumns ?? new List<ProjectionSourceColumn>();
            }
            catch (Exception ex)
            {
                // The statement may not be describable, or the dynamic management function may be
                // unavailable, as on a dedicated SQL pool. Reporting nothing leaves the behavior to
                // the missing-primary-key path, which is where it was before this change.
                _logger.LogWarning(
                    "Unable to describe the projection of {selectStatement} to infer a key: {message}",
                    selectStatement,
                    ex.Message);

                return new List<ProjectionSourceColumn>();
            }
        }

        private async Task<List<ProjectionSourceColumn>> SummarizeProjectionSourceColumnsAsync(
            DbDataReader reader,
            List<string>? args = null)
        {
            DbResultSet resultSet = await QueryExecutor.ExtractResultSetFromDbDataReaderAsync(reader);

            List<ProjectionSourceColumn> projectionColumns = new();

            foreach (DbResultSetRow row in resultSet.Rows)
            {
                if (row.Columns["COLUMN_NAME"] is not string columnName)
                {
                    continue;
                }

                projectionColumns.Add(new ProjectionSourceColumn
                {
                    Name = columnName,
                    SourceSchema = row.Columns["SOURCE_SCHEMA"] as string,
                    SourceTable = row.Columns["SOURCE_TABLE"] as string,
                    SourceColumn = row.Columns["SOURCE_COLUMN"] as string,
                    IsHidden = row.Columns["IS_HIDDEN"] is bool isHidden && isHidden
                });
            }

            return projectionColumns;
        }

        /// <summary>
        /// One column of a described projection, with the catalog object it resolves to.
        /// </summary>
        private sealed class ProjectionSourceColumn
        {
            public string Name { get; init; } = string.Empty;

            public string? SourceSchema { get; init; }

            public string? SourceTable { get; init; }

            public string? SourceColumn { get; init; }

            public bool IsHidden { get; init; }
        }

        /// <summary>
        /// One unique index of a database object, while its key columns are being collected.
        /// </summary>
        private sealed class CatalogIndexKey
        {
            public bool IsPrimaryKey { get; init; }

            public List<(string ColumnName, byte KeyOrdinal)> Columns { get; } = new();
        }

        public override string GetDefaultSchemaName()
        {
            return "dbo";
        }

        /// <summary>
        /// Takes a string version of an SQL Server data type (also applies to Azure SQL DB)
        /// and returns its .NET common language runtime (CLR) counterpart
        /// As per https://docs.microsoft.com/dotnet/framework/data/adonet/sql-server-data-type-mappings
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            return TypeHelper.GetSystemTypeFromSqlDbType(sqlType);
        }

        /// <inheritdoc/>
        public override async Task PopulateTriggerMetadataForTable(string entityName, string schemaName, string tableName, SourceDefinition sourceDefinition)
        {
            string enumerateEnabledTriggers = SqlQueryBuilder.BuildFetchEnabledTriggersQuery();
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}param0", new(schemaName, DbType.String) },
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}param1", new(tableName, DbType.String) }
            };

            JsonArray? resultArray = await QueryExecutor.ExecuteQueryAsync(
                sqltext: enumerateEnabledTriggers,
                parameters: parameters,
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync,
                dataSourceName: _dataSourceName);
            using JsonDocument sqlResult = JsonDocument.Parse(resultArray!.ToJsonString());

            foreach (JsonElement element in sqlResult.RootElement.EnumerateArray())
            {
                string type_desc = element.GetProperty("type_desc").ToString();
                if ("UPDATE".Equals(type_desc))
                {
                    sourceDefinition.IsUpdateDMLTriggerEnabled = true;
                    if (!_isValidateOnly)
                    {
                        _logger.LogInformation($"An update trigger is enabled for the entity: {entityName}");
                    }
                }

                if ("INSERT".Equals(type_desc))
                {
                    sourceDefinition.IsInsertDMLTriggerEnabled = true;
                    if (!_isValidateOnly)
                    {
                        _logger.LogInformation($"An insert trigger is enabled for the entity: {entityName}");
                    }
                }
            }
        }

        /// <inheritdoc/>
        protected override void PopulateColumnDefinitionWithHasDefaultAndDbType(
            SourceDefinition sourceDefinition,
            DataTable allColumnsInTable)
        {
            foreach (DataRow columnInfo in allColumnsInTable.Rows)
            {
                string columnName = (string)columnInfo["COLUMN_NAME"];
                bool hasDefault =
                    Type.GetTypeCode(columnInfo["COLUMN_DEFAULT"].GetType()) != TypeCode.DBNull;
                if (sourceDefinition.Columns.TryGetValue(columnName, out ColumnDefinition? columnDefinition))
                {
                    columnDefinition.HasDefault = hasDefault;

                    if (hasDefault)
                    {
                        columnDefinition.DefaultValue = columnInfo["COLUMN_DEFAULT"];
                    }

                    columnDefinition.DbType = TypeHelper.GetDbTypeFromSystemType(columnDefinition.SystemType);

                    string sqlDbTypeName = (string)columnInfo["DATA_TYPE"];

                    if (columnDefinition.SystemType == typeof(SqlVector<Single>))
                    {
                        sqlDbTypeName = "vector";   // Currently the "DATA_TYPE" column returns "varbinary" for vector type columns. This is a known issue https://learn.microsoft.com/en-us/sql/t-sql/data-types/vector-data-type?view=sql-server-ver17&tabs=csharp#known-issues
                        columnDefinition.IsArrayType = true;
                        columnDefinition.ElementSystemType = typeof(Single);
                        columnDefinition.SystemType = columnDefinition.ElementSystemType.MakeArrayType();
                    }

                    if (Enum.TryParse(sqlDbTypeName, ignoreCase: true, out SqlDbType sqlDbType))
                    {
                        // The DbType enum in .NET does not distinguish between VarChar and NVarChar. Both are mapped to DbType.String.
                        // So to keep track of the underlying sqlDbType, we store it in the columnDefinition.
                        columnDefinition.SqlDbType = sqlDbType;
                    }

                    if (columnDefinition.SystemType == typeof(DateTime) || columnDefinition.SystemType == typeof(DateTimeOffset))
                    {
                        // MsSql types like date,smalldatetime,datetime,datetime2 are mapped to the same .NET type of DateTime.
                        // Thus to determine the actual dbtype, we use the underlying MsSql type instead of the .NET type.
                        DbType dbType;
                        string sqlType = (string)columnInfo["DATA_TYPE"];
                        if (TryResolveDbType(sqlType, out dbType))
                        {
                            columnDefinition.DbType = dbType;
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        protected override async Task FillSchemaForStoredProcedureAsync(
            Entity procedureEntity,
            string entityName,
            string schemaName,
            string storedProcedureSourceName,
            StoredProcedureDefinition storedProcedureDefinition)
        {
            using DbConnection conn = new SqlConnection();
            conn.ConnectionString = ConnectionString;
            await QueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, _dataSourceName);
            await conn.OpenAsync();

            string[] procedureRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            // To restrict the parameters for the current stored procedure, specify its name
            procedureRestrictions[0] = conn.Database;
            procedureRestrictions[1] = schemaName;
            procedureRestrictions[2] = storedProcedureSourceName;

            DataTable procedureMetadata = await conn.GetSchemaAsync(collectionName: "Procedures", restrictionValues: procedureRestrictions);

            // Stored procedure does not exist in DB schema
            if (procedureMetadata.Rows.Count == 0)
            {
                throw new DataApiBuilderException(
                    message: $"No stored procedure definition found for the given database object {storedProcedureSourceName}",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            // Each row in the procedureParams DataTable corresponds to a single parameter
            DataTable parameterMetadata = await conn.GetSchemaAsync(collectionName: "ProcedureParameters", restrictionValues: procedureRestrictions);

            // For each row/parameter, add an entry to StoredProcedureDefinition.Parameters dictionary
            foreach (DataRow row in parameterMetadata.Rows)
            {
                // row["DATA_TYPE"] has value type string so a direct cast to System.Type is not supported.
                // See https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
                string sqlType = (string)row["DATA_TYPE"];
                Type systemType = SqlToCLRType(sqlType);
                ParameterDefinition paramDefinition = new()
                {
                    SystemType = systemType,
                    DbType = TypeHelper.GetDbTypeFromSystemType(systemType)
                };

                if (paramDefinition.SystemType == typeof(DateTime) || paramDefinition.SystemType == typeof(DateTimeOffset))
                {
                    // MsSql types like date,smalldatetime,datetime,datetime2 are mapped to the same .NET type of DateTime.
                    // Thus to determine the actual dbtype, we use the underlying MsSql type instead of the .NET type.
                    DbType dbType;
                    if (TryResolveDbType(sqlType, out dbType))
                    {
                        paramDefinition.DbType = dbType;
                    }
                }

                // Add to parameters dictionary without the leading @ sign
                storedProcedureDefinition.Parameters.TryAdd(((string)row["PARAMETER_NAME"])[1..], paramDefinition);
            }

            // Loop through parameters specified in config, throw error if not found in schema
            // else set runtime config defined default values.
            // Note: we defer type checking of parameters specified in config until request time
            List<ParameterMetadata>? configParameters = procedureEntity.Source.Parameters;
            if (configParameters is not null)
            {
                foreach (ParameterMetadata paramMetadata in configParameters)
                {
                    string configParamKey = paramMetadata.Name;
                    object? configParamValue = paramMetadata.Default;

                    if (!storedProcedureDefinition.Parameters.TryGetValue(configParamKey, out ParameterDefinition? parameterDefinition))
                    {
                        throw new DataApiBuilderException(
                            message: $"Could not find parameter \"{configParamKey}\" specified in config for procedure \"{schemaName}.{storedProcedureSourceName}\"",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }
                    else
                    {
                        parameterDefinition.Description = paramMetadata.Description;
                        parameterDefinition.Required = paramMetadata.Required;
                        parameterDefinition.Default = paramMetadata.Default;
                        parameterDefinition.HasConfigDefault = paramMetadata.Default is not null;
                        parameterDefinition.ConfigDefaultValue = paramMetadata.Default?.ToString();
                    }
                }
            }

            // Generating exposed stored-procedure query/mutation name and adding to the dictionary mapping it to its entity name.
            GraphQLStoredProcedureExposedNameToEntityNameMap.TryAdd(GenerateStoredProcedureGraphQLFieldName(entityName, procedureEntity), entityName);
        }

        /// <inheritdoc/>
        protected override void PopulateMetadataForLinkingObject(
            string entityName,
            string targetEntityName,
            string linkingObject,
            Dictionary<string, DatabaseObject> sourceObjects)
        {
            if (!_runtimeConfigProvider.GetConfig().IsMultipleCreateOperationEnabled())
            {
                // Currently we have this same class instantiated for both MsSql and DwSql.
                // This is a refactor we need to take care of in future.
                return;
            }

            string linkingEntityName = GraphQLUtils.GenerateLinkingEntityName(entityName, targetEntityName);

            // Create linking entity with disabled REST/GraphQL endpoints.
            // Even though GraphQL endpoint is disabled, we will be able to later create an object type definition
            // for this linking entity (which is later used to generate source->target linking object definition)
            // because the logic for creation of object definition for linking entity does not depend on whether
            // GraphQL is enabled/disabled. The linking object definitions are not exposed in the schema to the user.
            Entity linkingEntity = new(
                Source: new EntitySource(Type: EntitySourceType.Table, Object: linkingObject, Parameters: null, KeyFields: null),
                Fields: null,
                Rest: new(Array.Empty<SupportedHttpVerb>(), Enabled: false),
                GraphQL: new(Singular: linkingEntityName, Plural: linkingEntityName, Enabled: false),
                Permissions: Array.Empty<EntityPermission>(),
                Relationships: null,
                Mappings: new(),
                IsLinkingEntity: true);
            _linkingEntities.TryAdd(linkingEntityName, linkingEntity);
            PopulateDatabaseObjectForEntity(linkingEntity, linkingEntityName, sourceObjects);
        }

        /// <summary>
        /// Takes a string version of a sql date/time type and returns its corresponding DbType.
        /// </summary>
        /// <param name="sqlDbTypeName">Name of the sqlDbType.<</param>
        /// <param name="dbType">DbType of the parameter corresponding to its sqlDbTypeName.</param>
        /// <returns>Returns true when the given sqlDbTypeName datetime type is supported by DAB and resolve it to its corresponding DbType, else false.</returns>
        private bool TryResolveDbType(string sqlDbTypeName, out DbType dbType)
        {
            if (Enum.TryParse(sqlDbTypeName, ignoreCase: true, out SqlDbType sqlDbType))
            {
                // For MsSql, all the date time types i.e. date, smalldatetime, datetime, datetime2 map to System.DateTime system type.
                // Hence we cannot directly determine the DbType from the system type.
                // However, to make sure that the database correctly interprets these datatypes, it is necessary to correctly
                // populate the DbTypes.
                return TypeHelper.TryGetDbTypeFromSqlDbDateTimeType(sqlDbType, out dbType);
            }
            else
            {
                // This code should never be hit because every sqlDbTypeName must have a corresponding sqlDbType.
                // However, when a new data type is introduced in MsSql which maps to .NET type of DateTime, this code block
                // will be hit. Returning false instead of throwing an exception in that case prevents the engine from crashing.
                _logger.LogWarning("Could not determine DbType for SqlDb type of {sqlDbTypeName}", sqlDbTypeName);
                dbType = 0;
                return false;
            }
        }

        /// <inheritdoc/>
        protected override async Task GenerateAutoentitiesIntoEntities(IReadOnlyDictionary<string, Autoentity>? autoentities)
        {
            if (autoentities is null)
            {
                return;
            }

            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            Dictionary<string, Entity> entities = new();
            Dictionary<string, string> entityNameToRawEntity = new();
            foreach ((string autoentityName, Autoentity autoentity) in autoentities)
            {
                int addedEntities = 0;
                JsonArray? resultArray = await QueryAutoentitiesAsync(autoentityName, autoentity);
                if (resultArray is null)
                {
                    continue;
                }

                foreach (JsonObject? resultObject in resultArray)
                {
                    if (resultObject is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"Cannot create new entity from autoentities definition '{autoentityName}' due to an internal error.",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }

                    // Extract the entity name, schema, and database object name from the query result.
                    // The SQL query returns these values with placeholders already replaced.
                    string? entityName = resultObject["entity_name"]?.ToString();
                    string? objectName = resultObject["object"]?.ToString();
                    string? schemaName = resultObject["schema"]?.ToString();

                    if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(schemaName))
                    {
                        _logger.LogError("Skipping autoentity generation: 'entity_name', 'object', or 'schema' is null or empty for autoentities definition '{autoentityName}'.", autoentityName);
                        continue;
                    }

                    // Remove whitespace from the entity name and camelCase-join words so the result is
                    // a valid identifier for REST paths and GraphQL singular/plural names.
                    string rawEntityName = entityName;
                    entityName = RemoveWhitespaceAddCamelCase(entityName);

                    if (string.IsNullOrEmpty(entityName))
                    {
                        _logger.LogError(
                            "Skipping autoentity generation: entity name '{rawEntityName}' for schema '{schemaName}' resolves to an empty string after whitespace removal for autoentities definition '{autoentityName}'.",
                            rawEntityName, schemaName, autoentityName);
                        continue;
                    }

                    if (rawEntityName != entityName)
                    {
                        _logger.LogDebug(
                            "Entity name '{rawEntityName}' was normalized to '{entityName}' by removing whitespace.",
                            rawEntityName, entityName);
                    }

                    // Create the entity using the template settings and permissions from the autoentity configuration.
                    // Currently the source type is always Table for auto-generated entities from database objects.
                    Entity generatedEntity = new(
                        Source: new EntitySource(
                            Object: $"{schemaName}.{objectName}",
                            Type: EntitySourceType.Table,
                            Parameters: null,
                            KeyFields: null),
                        GraphQL: autoentity.Template.GraphQL,
                        Rest: autoentity.Template.Rest,
                        Mcp: autoentity.Template.Mcp,
                        Permissions: autoentity.Permissions,
                        Cache: autoentity.Template.Cache,
                        Health: autoentity.Template.Health,
                        Fields: null,
                        Relationships: null,
                        Mappings: new(),
                        IsAutoentity: true);

                    // Add the generated entity to the linking entities dictionary.
                    // This allows the entity to be processed later during metadata population.
                    // A collision can occur when two database objects produce the same entity name after
                    // whitespace removal (e.g. "Order Item" and "OrderItem" both yield "OrderItem").
                    if (!entities.TryAdd(entityName, generatedEntity) || !runtimeConfig.TryAddGeneratedAutoentityNameToDataSourceName(entityName, autoentityName))
                    {
                        string checkEntityName = entityNameToRawEntity.ContainsKey(entityName) && !rawEntityName.Contains(" ")
                            ? entityNameToRawEntity[entityName]
                            : rawEntityName;
                        string collisionMessage = checkEntityName.Contains(" ")
                            ? $"Entity '{entityName}' normalized from '{checkEntityName}' from '{schemaName}' schema conflicts in autoentity pattern '{autoentityName}'. Use --patterns.exclude to skip it."
                            : $"Entity '{entityName}' conflicts in autoentity pattern '{autoentityName}'. Use --patterns.exclude to skip it.";
                        throw new DataApiBuilderException(
                            message: collisionMessage,
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }

                    addedEntities++;
                    entityNameToRawEntity.Add(entityName, rawEntityName);
                }

                if (addedEntities == 0)
                {
                    _logger.LogWarning("No new entities were generated from the autoentities definition '{autoentityName}'.", autoentityName);
                }

                // Track resolution count for validation.
                runtimeConfig.AutoentityResolutionCounts[autoentityName] = addedEntities;
            }

            LogRestPathsForEntities(runtimeConfig, entities);
            _runtimeConfigProvider.AddMergedEntitiesToConfig(entities);
        }

        /// <summary>
        /// Queries the database for autoentities based on the provided autoentity definition.
        /// </summary>
        /// <param name="autoentityName">The name of the autoentity definition.</param>
        /// <param name="autoentity">The autoentity definition containing patterns for inclusion, exclusion, and name.</param>
        /// <returns>A JsonArray containing the queried autoentities, or an empty array if none are found.</returns>
        public async Task<JsonArray?> QueryAutoentitiesAsync(string autoentityName, Autoentity autoentity)
        {
            string include = string.Join(",", autoentity.Patterns.Include);
            string exclude = string.Join(",", autoentity.Patterns.Exclude);
            string namePattern = autoentity.Patterns.Name;
            string getAutoentitiesQuery = SqlQueryBuilder.BuildGetAutoentitiesQuery();
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}include_pattern", new(include, null, SqlDbType.NVarChar) },
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}exclude_pattern", new(exclude, null, SqlDbType.NVarChar) },
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}name_pattern", new(namePattern, null, SqlDbType.NVarChar) }
            };

            _logger.LogDebug("Query for autoentities is being executed with the following parameters.");
            _logger.LogDebug("The autoentities definition '{autoentityName}' include pattern: {include}", autoentityName, include);
            _logger.LogDebug("The autoentities definition '{autoentityName}' exclude pattern: {exclude}", autoentityName, exclude);
            _logger.LogDebug("The autoentities definition '{autoentityName}' name pattern: {namePattern}", autoentityName, namePattern);

            JsonArray? resultArray = await QueryExecutor.ExecuteQueryAsync(
                sqltext: getAutoentitiesQuery,
                parameters: parameters,
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync,
                dataSourceName: _dataSourceName);

            return resultArray;
        }
    }
}

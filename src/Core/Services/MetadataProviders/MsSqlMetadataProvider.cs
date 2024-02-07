// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using Microsoft.Data.SqlClient;
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
        public MsSqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
            : base(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly)
        {
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
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync);
            using JsonDocument sqlResult = JsonDocument.Parse(resultArray!.ToJsonString());

            foreach (JsonElement element in sqlResult.RootElement.EnumerateArray())
            {
                string type_desc = element.GetProperty("type_desc").ToString();
                if ("UPDATE".Equals(type_desc))
                {
                    sourceDefinition.IsUpdateDMLTriggerEnabled = true;
                    _logger.LogInformation($"An update trigger is enabled for the entity: {entityName}");
                }

                if ("INSERT".Equals(type_desc))
                {
                    sourceDefinition.IsInsertDMLTriggerEnabled = true;
                    _logger.LogInformation($"An insert trigger is enabled for the entity: {entityName}");
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
            Dictionary<string, object>? configParameters = procedureEntity.Source.Parameters;
            if (configParameters is not null)
            {
                foreach ((string configParamKey, object configParamValue) in configParameters)
                {
                    if (!storedProcedureDefinition.Parameters.TryGetValue(configParamKey, out ParameterDefinition? parameterDefinition))
                    {
                        throw new DataApiBuilderException(
                            message: $"Could not find parameter \"{configParamKey}\" specified in config for procedure \"{schemaName}.{storedProcedureSourceName}\"",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }
                    else
                    {
                        parameterDefinition.HasConfigDefault = true;
                        parameterDefinition.ConfigDefaultValue = configParamValue?.ToString();
                    }
                }
            }

            // Generating exposed stored-procedure query/mutation name and adding to the dictionary mapping it to its entity name.
            GraphQLStoredProcedureExposedNameToEntityNameMap.TryAdd(GenerateStoredProcedureGraphQLFieldName(entityName, procedureEntity), entityName);
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
        protected override void FillInferredFkInfo(
            IEnumerable<SourceDefinition> dbEntitiesToBePopulatedWithFK)
        {
            // Maintain a set of relationship pairs for which an FK constraint should not exist in the database.
            // We don't want circular FK constraints i.e. both an FK constraint from source -> target and from target -> source for nested insertions.
            // If we find an FK constraint for source -> target, we add an entry to this set for target -> source, indicating
            // we should not have an FK constraint from target -> source, as that would lead to circular relationships, in which case
            // we cannot perform nested insertion.
            HashSet<RelationShipPair> prohibitedRelationshipPairs = new();

            // For each table definition that has to be populated with the inferred
            // foreign key information.
            foreach (SourceDefinition sourceDefinition in dbEntitiesToBePopulatedWithFK)
            {
                // For each source entities, which maps to this table definition
                // and has a relationship metadata to be filled.
                foreach ((string sourceEntityName, RelationshipMetadata relationshipData)
                       in sourceDefinition.SourceEntityRelationshipMap)
                {
                    // Enumerate all the foreign keys required for all the target entities
                    // that this source is related to.
                    foreach ((string targetEntityName, List<ForeignKeyDefinition> foreignKeyDefinitionsToTarget) in relationshipData.TargetEntityToFkDefinitionMap)
                    {
                        List<ForeignKeyDefinition> validateForeignKeyDefinitionsToTarget = new();
                        // For each foreign key between this pair of source and target entities
                        // which needs the referencing columns,
                        // find the fk inferred for this pair the backend and
                        // equate the referencing columns and referenced columns.
                        foreach (ForeignKeyDefinition foreignKeyDefinitionToTarget in foreignKeyDefinitionsToTarget)
                        {
                            // Add the referencing and referenced columns for this foreign key definition for the target.
                            if (PairToFkDefinition is not null &&
                                PairToFkDefinition.TryGetValue(foreignKeyDefinitionToTarget.Pair, out ForeignKeyDefinition? inferredDefinition))
                            {
                                if (prohibitedRelationshipPairs.Contains(foreignKeyDefinitionToTarget.Pair))
                                {
                                    // This means that there are 2 relationships defined in the database:
                                    // 1. From source to target
                                    // 2. From target to source
                                    // It is not possible to determine the direction of relationship in such a case, so we throw an exception.
                                    throw new DataApiBuilderException(
                                        message: $"Circular relationship detected between source entity: {sourceEntityName} and target entity: {targetEntityName}. Cannot support nested insertion.",
                                        statusCode: HttpStatusCode.ServiceUnavailable,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                                }

                                // Add an entry to inverseFKPairs to track what all (target, source) pairings are not allowed to
                                // have a relationship in the database in order to support nested insertion.
                                prohibitedRelationshipPairs.Add(new(foreignKeyDefinitionToTarget.Pair.ReferencedDbTable, foreignKeyDefinitionToTarget.Pair.ReferencingDbTable));

                                // if the referencing and referenced columns count > 0,
                                // we have already gathered this information from the runtime config.
                                if (foreignKeyDefinitionToTarget.ReferencingColumns.Count > 0 && foreignKeyDefinitionToTarget.ReferencedColumns.Count > 0)
                                {
                                    if (!AreFKDefinitionsEqual(foreignKeyDefinitionToTarget, inferredDefinition))
                                    {
                                        throw new DataApiBuilderException(
                                            message: $"The relationship defined between source entity: {sourceEntityName} and target entity: {targetEntityName} in the config conflicts" +
                                            $" with the relationship defined in the database.",
                                            statusCode: HttpStatusCode.ServiceUnavailable,
                                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                                    }
                                    else
                                    {
                                        validateForeignKeyDefinitionsToTarget.Add(foreignKeyDefinitionToTarget);
                                    }
                                }
                                // Only add the referencing/referenced columns if they have not been
                                // specified in the configuration file.
                                else
                                {
                                    validateForeignKeyDefinitionsToTarget.Add(inferredDefinition);
                                }
                            }
                        }

                        foreach(ForeignKeyDefinition foreignKeyDefinitionToTarget in foreignKeyDefinitionsToTarget)
                        {
                            if (PairToFkDefinition is not null &&
                                !PairToFkDefinition.ContainsKey(foreignKeyDefinitionToTarget.Pair) &&
                                !prohibitedRelationshipPairs.Contains(foreignKeyDefinitionToTarget.Pair))
                            {
                                validateForeignKeyDefinitionsToTarget.Add(foreignKeyDefinitionToTarget);
                            }
                        }

                        relationshipData.TargetEntityToFkDefinitionMap[targetEntityName] = validateForeignKeyDefinitionsToTarget;
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to compare two foreign key definitions for equality on the basis of the referencing -> referenced column mappings present in them.
        /// The equality ensures that both the foreign key definitions have:
        /// 1. Same set of referencing and referenced tables,
        /// 2. Same number of referencing/referenced columns,
        /// 3. Same mappings from referencing -> referenced column.
        /// </summary>
        /// <param name="fkDefinition1">First foreign key definition.</param>
        /// <param name="fkDefinition2">Second foreign key definition.</param>
        /// <returns>true if all the above mentioned conditions are met, else false.</returns>
        private static bool AreFKDefinitionsEqual(ForeignKeyDefinition fkDefinition1, ForeignKeyDefinition fkDefinition2)
        {
            if (!fkDefinition1.Pair.Equals(fkDefinition2.Pair) || fkDefinition1.ReferencingColumns.Count != fkDefinition2.ReferencingColumns.Count)
            {
                return false;
            }

            Dictionary<string, string> referencingToReferencedColumns = fkDefinition1.ReferencingColumns.Zip(
                fkDefinition1.ReferencedColumns, (key, value) => new { Key = key, Value = value }).ToDictionary(item => item.Key, item => item.Value);

            // Traverse through each (referencing, referenced) columns pair in the second foreign key definition.
            for (int idx = 0; idx < fkDefinition2.ReferencingColumns.Count; idx++)
            {
                string referencingColumnName = fkDefinition2.ReferencingColumns[idx];
                if (!referencingToReferencedColumns.TryGetValue(referencingColumnName, out string? referencedColumnName)
                    || !referencedColumnName.Equals(fkDefinition2.ReferencedColumns[idx]))
                {
                    // This indicates that either there is no mapping defined for referencingColumnName in the second foreign key definition
                    // or the referencing -> referenced column mapping in the second foreign key definition do not match the mapping in the first foreign key definition.
                    // In both the cases, it is implied that the two foreign key definitions do not match.
                    return false;
                }
            }

            return true;
        }
    }
}

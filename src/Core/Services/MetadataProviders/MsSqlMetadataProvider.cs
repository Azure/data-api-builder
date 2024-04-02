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
using Azure.DataApiBuilder.Service.GraphQLBuilder;
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
        private RuntimeConfigProvider _runtimeConfigProvider;

        public MsSqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
            : base(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
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

                    string sqlDbTypeName = (string)columnInfo["DATA_TYPE"];
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
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.DatabasePrimitives;
using System.Data;
using System.Text.Json.Nodes;
using System.Text.Json;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

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
            IQueryExecutor queryExecutor,
            IQueryBuilder sqlQueryBuilder,
            ILogger<ISqlMetadataProvider> logger)
            : base(runtimeConfigProvider, queryExecutor, sqlQueryBuilder, logger)
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

        public override async Task PopulateTriggerMetadataForTable(string schemaName, string tableName, SourceDefinition sourceDefinition)
        {
            string queryToGetNumberOfEnabledTriggers = SqlQueryBuilder.GetQueryToGetEnabledTriggers();
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}param0", new(schemaName, DbType.String) },
                { $"{BaseQueryStructure.PARAM_NAME_PREFIX}param1", new(tableName, DbType.String) }
            };

            JsonArray? resultArray = await QueryExecutor.ExecuteQueryAsync(
                sqltext: queryToGetNumberOfEnabledTriggers,
                parameters: parameters!,
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync);
            using JsonDocument sqlResult = JsonDocument.Parse(resultArray!.ToJsonString());

            foreach (JsonElement element in sqlResult.RootElement.EnumerateArray())
            {
                string type_desc = element.GetProperty("type_desc").ToString();
                if ("UPDATE".Equals(type_desc))
                {
                    sourceDefinition.IsUpdateDMLTriggerEnabled = true;
                }

                if ("INSERT".Equals(type_desc))
                {
                    sourceDefinition.IsInsertDMLTriggerEnabled = true;
                }
            }
        }
    }
}

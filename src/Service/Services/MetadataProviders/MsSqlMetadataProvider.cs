// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Services
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

        /// <summary>
        /// Ideally should check if a default is set in sql by parsing procedure's object definition.
        /// See https://docs.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-parameters-transact-sql?view=sql-server-ver16#:~:text=cursor%2Dreference%20parameter.-,has_default_value,-bit
        /// For SQL Server not populating this metadata for us; MySQL doesn't seem to allow parameter defaults so not relevant.
        /// </summary>
        /// <param name="schemaName">The name of the schema.</param>
        /// <param name="storedProcedureName">The name of the stored procedure.</param>
        /// <param name="storedProcedureDefinition">The definition of the stored procedure whose parameters to populate with optionality.</param>
        /// <returns></returns>
        protected override async Task PopulateParameterOptionalityForStoredProcedureAsync(
            string schemaName,
            string storedProcedureName,
            StoredProcedureDefinition storedProcedureDefinition)
        {
            string dbStoredProcedureName = $"{schemaName}.{storedProcedureName}";
            string queryForParameterNullability = SqlQueryBuilder.BuildStoredProcedureDefinitionQuery(
                dbStoredProcedureName);
            JsonArray? result = await QueryExecutor.ExecuteQueryAsync(
                sqltext: queryForParameterNullability,
                parameters: null!,
                dataReaderHandler: QueryExecutor.GetJsonArrayAsync);

            if (result == null || result.Count < 1)
            {
                throw new DataApiBuilderException(
                    message: "There was a problem inspecting parameter nullability"
                        + $"for Stored Procedure {dbStoredProcedureName}."
                        + "Received no result set.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            JsonNode? resultJson;
            if (result.Count > 1 || (resultJson = result[0]) == null)
            {
                throw new DataApiBuilderException(
                    message: "There was a problem inspecting parameter nullability"
                        + $"for Stored Procedure {dbStoredProcedureName}."
                        + "Received an invalid result set.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            using JsonDocument resultDocument = JsonDocument.Parse(resultJson.ToJsonString());
            JsonElement rootElement = resultDocument.RootElement;
            string? procedureDefinition = rootElement.GetProperty("ProcedureDefinition").ToString();

            // See regexr.com/7c7um for this regex and it's associated tests.
            Regex? regex = new(@"@([\w]+)\s+([^\s]+)\s*=\s*([^, ]*),?", RegexOptions.IgnoreCase);
            MatchCollection? matches = regex.Matches(procedureDefinition);
            foreach (Match match in matches)
            {
                string? sqlParamName = match.Groups[1]?.Value;
                string? sqlParamType = match.Groups[2]?.Value;
                string? sqlParamDefaultValue = match.Groups[3]?.Value;
                if (sqlParamName != null && sqlParamDefaultValue != null)
                {
                    storedProcedureDefinition.Parameters[sqlParamName].IsOptional = true;
                }
            }
        }

        public override string GetDefaultSchemaName()
        {
            return "dbo";
        }

        /// <summary>
        /// Takes a string version of an MS SQL data type and returns its .NET common language runtime (CLR) counterpart
        /// As per https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            switch (sqlType)
            {
                case "bigint":
                case "real":
                    return typeof(long);
                case "numeric":
                    return typeof(decimal);
                case "bit":
                    return typeof(bool);
                case "smallint":
                    return typeof(short);
                case "decimal":
                case "smallmoney":
                case "money":
                    return typeof(decimal);
                case "int":
                    return typeof(int);
                case "tinyint":
                    return typeof(byte);
                case "float":
                    return typeof(float);
                case "date":
                case "datetime2":
                case "smalldatetime":
                case "datetime":
                case "time":
                    return typeof(DateTime);
                case "datetimeoffset":
                    return typeof(DateTimeOffset);
                case "char":
                case "varchar":
                case "text":
                case "nchar":
                case "nvarchar":
                case "ntext":
                    return typeof(string);
                case "binary":
                case "varbinary":
                case "image":
                    return typeof(byte[]);
                case "uniqueidentifier":
                    return typeof(Guid);
                default:
                    throw new DataApiBuilderException(message: $"Tried to convert unsupported data type: {sqlType}",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }
        }

        /// <summary>
        /// Takes a string version of an MS SQL parameter mode and returns its .NET common language runtime (CLR) counterpart.
        /// </summary>
        public override ParameterDirection ToParameterDirectionEnum(string parameterDirection)
        {
            switch (parameterDirection)
            {
                case "IN":
                    return ParameterDirection.Input;
                case "OUT":
                    return ParameterDirection.Output;
                case "INOUT":
                    return ParameterDirection.InputOutput;
                default:
                    throw new DataApiBuilderException(message: $"Tried to convert unsupported parameter mode: {parameterDirection}",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }
        }
    }
}

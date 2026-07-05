// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Net;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// PostgreSql specific override for SqlMetadataProvider.
    /// All the method definitions from base class are sufficient
    /// this class is only created for symmetricity with MySql
    /// and ease of expanding the generics specific to PostgreSql.
    /// </summary>
    public class PostgreSqlMetadataProvider :
        SqlMetadataProvider<NpgsqlConnection, NpgsqlDataAdapter, NpgsqlCommand>
    {

        public PostgreSqlMetadataProvider(
            RuntimeConfigProvider runtimeConfigProvider,
            RuntimeConfigValidator runtimeConfigValidator,
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            string dataSourceName,
            bool isValidateOnly = false)
            : base(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory, logger, dataSourceName, isValidateOnly)
        {
        }

        /// <summary>
        /// Only used for PostgreSql.
        /// The connection string could contain the schema,
        /// in which case it will be associated with the
        /// property 'SearchPath' in the string builder we create.
        /// If `SearchPath` is null we assign the empty string to the
        /// the out param schemaName, otherwise we assign the
        /// value associated with `SearchPath`.
        /// </summary>
        /// <param name="schemaName">the schema name we save.</param>
        /// <returns>true if non empty schema in connection string, false otherwise.</returns>
        public static bool TryGetSchemaFromConnectionString(string connectionString, out string schemaName)
        {
            NpgsqlConnectionStringBuilder connectionStringBuilder;
            try
            {
                connectionStringBuilder = new(connectionString);
            }
            catch (Exception ex)
            {
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization,
                    innerException: ex);
            }

            schemaName = connectionStringBuilder.SearchPath is null ? string.Empty : connectionStringBuilder.SearchPath;
            return string.IsNullOrEmpty(schemaName) ? false : true;
        }

        public override string GetDefaultSchemaName()
        {
            return "public";
        }

        /// <summary>
        /// Takes a string version of a PostgreSql data type and returns its .NET common language runtime (CLR) counterpart
        /// TODO: For PostgreSql stored procedure support, this needs to be implemented.
        /// </summary>
        public override Type SqlToCLRType(string sqlType)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Maps PostgreSQL array udt_name prefixes to their CLR element types.
        /// PostgreSQL array types in information_schema use udt_name with a leading underscore
        /// (e.g., _int4 for int[], _text for text[]).
        /// </summary>
        private static readonly Dictionary<string, Type> _pgArrayUdtToElementType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["_int2"] = typeof(short),
            ["_int4"] = typeof(int),
            ["_int8"] = typeof(long),
            ["_float4"] = typeof(float),
            ["_float8"] = typeof(double),
            ["_numeric"] = typeof(decimal),
            ["_bool"] = typeof(bool),
            ["_text"] = typeof(string),
            ["_varchar"] = typeof(string),
            ["_bpchar"] = typeof(string),
            ["_uuid"] = typeof(Guid),
            ["_timestamp"] = typeof(DateTime),
            ["_timestamptz"] = typeof(DateTimeOffset),
            ["_json"] = typeof(string),
            ["_jsonb"] = typeof(string),
            ["_money"] = typeof(decimal),
        };

        /// <summary>
        /// Override to detect PostgreSQL array columns using information_schema metadata.
        /// Npgsql's DataAdapter reports array columns as System.Array (the abstract base class),
        /// so we use the data_type and udt_name from information_schema.columns to identify arrays
        /// and resolve their element types.
        /// </summary>
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

                    // Detect array columns: data_type is "ARRAY" in information_schema for PostgreSQL array types.
                    string dataType = columnInfo["DATA_TYPE"] is string dt ? dt : string.Empty;
                    if (string.Equals(dataType, "ARRAY", StringComparison.OrdinalIgnoreCase))
                    {
                        string udtName = columnInfo["UDT_NAME"] is string udt ? udt : string.Empty;
                        if (_pgArrayUdtToElementType.TryGetValue(udtName, out Type? elementType))
                        {
                            columnDefinition.IsArrayType = true;
                            columnDefinition.ElementSystemType = elementType;
                            columnDefinition.SystemType = elementType.MakeArrayType();
                            columnDefinition.IsReadOnly = true;
                        }
                    }

                    columnDefinition.DbType = TypeHelper.GetDbTypeFromSystemType(columnDefinition.SystemType);
                }
            }
        }
    }
}

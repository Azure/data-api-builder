using System;
using System.Data;
using System.Net;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.Identity;
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

        protected override string GetDefaultSchemaName()
        {
            return "dbo";
        }

        /// <summary>
        /// Using a data adapter, obtains the schema of the given table name
        /// and adds the corresponding entity in the data set.
        /// </summary>
        protected override async Task<DataTable> FillSchemaForTableAsync(
            string schemaName,
            string tableName)
        {
            using SqlConnection conn = new();
            // If connection string is set to empty string
            // we throw here to avoid having to sort out
            // complicated db specific exception messages.
            // This is caught and returned as DataApiBuilderException.
            // The runtime config has a public setter so we check
            // here for empty connection string to ensure that
            // it was not set to an invalid state after initialization.
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new DataApiBuilderException(
                    DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    " Connection string is null, empty, or whitespace.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            try
            {
                SqlConnectionStringBuilder connStringBuilder = new(ConnectionString);
                // for non-MySql DB types, this will throw an exception
                // for malformed connection strings
                conn.ConnectionString = ConnectionString;
                if (string.IsNullOrEmpty(connStringBuilder.UserID))
                {
                    DefaultAzureCredential credential = new();
                        /*(new DefaultAzureCredentialOptions
                        {
                          ManagedIdentityClientId = "2e2658cb-b558-4b57-80ec-da969eec38b8"
                        });*/
                    AccessToken accessToken =
                        await credential.GetTokenAsync(
                            new TokenRequestContext(new[] { MsSqlQueryExecutor.DATABASE_SCOPE }));
                    conn.AccessToken = accessToken.Token;
                }
            }
            catch (Exception ex)
            {
                string message = DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE +
                    $" Underlying Exception message: {ex.Message}";
                throw new DataApiBuilderException(
                    message,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            await conn.OpenAsync();

            SqlDataAdapter adapterForTable = new();
            SqlCommand selectCommand = new()
            {
                Connection = conn
            };

            string tablePrefix = GetTablePrefix(conn.Database, schemaName);
            selectCommand.CommandText
                = ($"SELECT * FROM {tablePrefix}.{SqlQueryBuilder.QuoteIdentifier(tableName)}");
            adapterForTable.SelectCommand = selectCommand;

            DataTable[] dataTable = adapterForTable.FillSchema(EntitiesDataSet, SchemaType.Source, tableName);
            return dataTable[0];
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
    }
}

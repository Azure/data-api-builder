using System;
using System.Net;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Azure.DataApiBuilder.Service.Services
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
            IQueryExecutor queryExecutor,
            IQueryBuilder sqlQueryBuilder,
            ILogger<ISqlMetadataProvider> logger)
            : base(runtimeConfigProvider, queryExecutor, sqlQueryBuilder, logger)
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
            catch (Exception)
            {
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            schemaName = connectionStringBuilder.SearchPath is null ? string.Empty : connectionStringBuilder.SearchPath;
            return string.IsNullOrEmpty(schemaName) ? false : true;
        }

        protected override string GetDefaultSchemaName()
        {
            return "public";
        }
    }
}

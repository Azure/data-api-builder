// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
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
    }
}

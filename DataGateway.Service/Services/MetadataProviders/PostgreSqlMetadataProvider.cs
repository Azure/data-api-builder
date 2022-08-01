using System;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Azure.DataGateway.Service.Services
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

        protected override string GetDefaultSchemaName()
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

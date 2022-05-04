using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.Extensions.Options;
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
            IOptions<RuntimeConfigPath> runtimeConfigPath,
            IQueryExecutor queryExecutor,
            IQueryBuilder sqlQueryBuilder)
            : base(runtimeConfigPath, queryExecutor, sqlQueryBuilder)
        {
        }

        protected override string GetDefaultSchemaName()
        {
            return "public";
        }
    }
}

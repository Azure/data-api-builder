using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
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
            IQueryBuilder sqlQueryBuilder)
            : base(runtimeConfigProvider, queryExecutor, sqlQueryBuilder)
        {
        }

        protected override string GetDefaultSchemaName()
        {
            return "public";
        }
    }
}

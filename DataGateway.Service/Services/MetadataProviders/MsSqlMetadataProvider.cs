using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.Data.SqlClient;

namespace Azure.DataGateway.Service.Services
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
            IQueryBuilder sqlQueryBuilder)
            : base(runtimeConfigProvider, queryExecutor, sqlQueryBuilder)
        {
        }

        protected override string GetDefaultSchemaName()
        {
            return "dbo";
        }
    }
}

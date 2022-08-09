using Azure.DataApiBuilder.Service.Configurations;
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

        protected override string GetDefaultSchemaName()
        {
            return "dbo";
        }
    }
}

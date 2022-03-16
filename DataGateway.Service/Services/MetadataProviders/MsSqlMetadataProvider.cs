using Microsoft.Data.SqlClient;

namespace Azure.DataGateway.Service.Services.MetadataProviders
{
    /// <summary>
    /// MsSQL specific override for SqlMetadataProvider
    /// </summary>
    public class MsSqlMetadataProvider :
        SqlMetadataProvider<SqlConnection, SqlDataAdapter, SqlCommand>
    {
        public MsSqlMetadataProvider(string connectionString)
            : base(connectionString)
        {
        }
    }
}

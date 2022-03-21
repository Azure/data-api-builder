using Azure.DataGateway.Service.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services.MetadataProviders
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
        public MsSqlMetadataProvider(IOptionsMonitor<DataGatewayConfig> dataGatewayConfig)
            : base(dataGatewayConfig)
        {
        }
    }
}

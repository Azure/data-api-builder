using Npgsql;

namespace Azure.DataGateway.Service.Services.MetadataProviders
{
    public class PostgreSqlMetadataProvider :
        SqlMetadataProvider<NpgsqlConnection, NpgsqlDataAdapter, NpgsqlCommand>
    {
        public PostgreSqlMetadataProvider(string connectionString)
        : base(connectionString)
        {
        }
    }
}

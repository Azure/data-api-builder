using Npgsql;

namespace Azure.DataGateway.Service.Services.MetadataProviders
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
        public PostgreSqlMetadataProvider(string connectionString)
            : base(connectionString)
        {
        }
    }
}

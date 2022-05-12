using System.Text.Json.Serialization;

namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Contains the information needed to connect to the backend database.
    /// </summary>
    /// <param name="DatabaseType">Specifies the kind of the backend database.</param>
    /// <param name="ConnectionString">The ADO.NET connection string that runtime
    /// will use to connect to the backend database.</param>
    public record DataSource(
        [property: JsonPropertyName(DataSource.DATABASE_PROPERTY_NAME)]
        DatabaseType DatabaseType,
        [property: JsonPropertyName(DataSource.RESOLVER_JSON_PROPERTY_NAME)]
        string? ResolverConfigFile)
    {
        public const string JSON_PROPERTY_NAME = "data-source";
        public const string DATABASE_PROPERTY_NAME = "database-type";
        public const string CONNSTRING_PROPERTY_NAME = "connection-string";
        public const string RESOLVER_JSON_PROPERTY_NAME = "resolver-config-file";

        public string GetDatabaseTypeNotSupportedMessage()
        {
            return string.Format("The provided database-type value: {0} is currently not supported." +
                "Please check the configuration file.", DatabaseType);
        }

        [property: JsonPropertyName(CONNSTRING_PROPERTY_NAME)]
        public string ConnectionString { get; set; } = string.Empty;
    }

    /// <summary>
    /// Options for CosmosDb database.
    /// </summary>
    public record CosmosDbOptions(string Database)
    {
        public const string JSON_PROPERTY_NAME = nameof(DatabaseType.cosmos);
    }

    /// <summary>
    /// Options for MsSql database.
    /// </summary>
    public record MsSqlOptions(
        [property: JsonPropertyName("set-session-context")]
        bool SetSessionContext = true)
    {
        public const string JSON_PROPERTY_NAME = nameof(DatabaseType.mssql);

        public MsSqlOptions()
            : this(SetSessionContext: true) { }
    }

    /// <summary>
    /// Options for PostgresSql database.
    /// </summary>
    public record PostgreSqlOptions
    {
        public const string JSON_PROPERTY_NAME = nameof(DatabaseType.postgresql);
    }

    /// <summary>
    /// Options for MySql database.
    /// </summary>
    public record MySqlOptions
    {
        public const string JSON_PROPERTY_NAME = nameof(DatabaseType.mysql);
    }

    /// <summary>
    /// Enum for the supported database types.
    /// </summary>
    public enum DatabaseType
    {
        cosmos,
        mssql,
        mysql,
        postgresql
    }
}

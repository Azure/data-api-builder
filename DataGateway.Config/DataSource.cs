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
        DatabaseType? DatabaseType,
        [property: JsonPropertyName(DataSource.CONNSTRING_PROPERTY_NAME)]
        string? ConnectionString,
        [property: JsonPropertyName(RuntimeConfig.RESOLVER_CONFIG_PROPERTY_NAME)]
        string? ResolverConfigFile,
        string? ResolverConfig,
        string? GraphQLSchema)
    {
        public const string CONFIG_PROPERTY_NAME = "data-source";
        public const string DATABASE_PROPERTY_NAME = "database-type";
        public const string CONNSTRING_PROPERTY_NAME = "connection-string";

        public DataSource() :
            this(
                DatabaseType: null,
                ConnectionString: null,
                ResolverConfigFile: null,
                ResolverConfig: null,
                GraphQLSchema: null)
        { }
    }

    /// <summary>
    /// Options for CosmosDb database.
    /// </summary>
    public record CosmosDbOptions(string Database)
    {
        public const string CONFIG_PROPERTY_NAME = nameof(DatabaseType.cosmos);
    }

    /// <summary>
    /// Options for MsSql database.
    /// </summary>
    public record MsSqlOptions(
        [property: JsonPropertyName("set-session-context")]
        bool SetSessionContext = true)
    {
        public const string CONFIG_PROPERTY_NAME = nameof(DatabaseType.mssql);

        public MsSqlOptions()
            : this(SetSessionContext: true) { }
    }

    /// <summary>
    /// Options for PostgresSql database.
    /// </summary>
    public record PostgreSqlOptions
    {
        public const string CONFIG_PROPERTY_NAME = nameof(DatabaseType.postgresql);
    }

    /// <summary>
    /// Options for MySql database.
    /// </summary>
    public record MySqlOptions
    {
        public const string CONFIG_PROPERTY_NAME = nameof(DatabaseType.mysql);
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

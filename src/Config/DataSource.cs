using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config
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
        [property: JsonPropertyName(DataSource.OPTIONS_PROPERTY_NAME)]
        object? DbOptions = null)
    {
        public const string JSON_PROPERTY_NAME = "data-source";
        public const string DATABASE_PROPERTY_NAME = "database-type";
        public const string CONNSTRING_PROPERTY_NAME = "connection-string";
        public const string OPTIONS_PROPERTY_NAME = "options";

        [property: JsonPropertyName(CONNSTRING_PROPERTY_NAME)]
        public string ConnectionString { get; set; } = string.Empty;
        public CosmosDbOptions? CosmosDbNoSql { get; set; }
        public CosmosDbPostgreSqlOptions? CosmosDbPostgreSql { get; set; }
        public MsSqlOptions? MsSql { get; set; }
        public PostgreSqlOptions? PostgreSql { get; set; }
        public MySqlOptions? MySql { get; set; }

        /// <summary>
        /// Method to populate the database specific options from the "options"
        /// section in data-source.
        /// </summary>
        public void PopulateDbSpecificOptions()
        {
            if (DbOptions is null)
            {
                return;
            }

            switch (DatabaseType)
            {
                case DatabaseType.cosmos:
                case DatabaseType.cosmosdb_nosql:
                    CosmosDbNoSql = ((JsonElement)DbOptions).Deserialize<CosmosDbOptions>(RuntimeConfig.SerializerOptions)!;
                    break;
                case DatabaseType.mssql:
                    MsSql = ((JsonElement)DbOptions).Deserialize<MsSqlOptions>(RuntimeConfig.SerializerOptions)!;
                    break;
                case DatabaseType.mysql:
                    MySql = ((JsonElement)DbOptions).Deserialize<MySqlOptions>(RuntimeConfig.SerializerOptions)!;
                    break;
                case DatabaseType.postgresql:
                    PostgreSql = ((JsonElement)DbOptions).Deserialize<PostgreSqlOptions>(RuntimeConfig.SerializerOptions)!;
                    break;
                case DatabaseType.cosmosdb_postgresql:
                    CosmosDbPostgreSql = ((JsonElement)DbOptions).Deserialize<CosmosDbPostgreSqlOptions>(RuntimeConfig.SerializerOptions)!;
                    break;
                default:
                    throw new Exception($"DatabaseType: ${DatabaseType} not supported.");
            }
        }
    }

    /// <summary>
    /// Options for CosmosDb database.
    /// </summary>
    public record CosmosDbOptions(
        string Database,
        string? Container,
        [property: JsonPropertyName(CosmosDbOptions.GRAPHQL_SCHEMA_PATH_PROPERTY_NAME)]
        string? GraphQLSchemaPath,
        [property: JsonIgnore]
        string? GraphQLSchema)
    {
        public const string GRAPHQL_SCHEMA_PATH_PROPERTY_NAME = "schema";
    }

    /// <summary>
    /// Options for MsSql database.
    /// </summary>
    public record MsSqlOptions(
        [property: JsonPropertyName("set-session-context")]
        [property: JsonIgnore]
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
    /// Options for CosmosDb_PostgresSql database.
    /// </summary>
    public record CosmosDbPostgreSqlOptions { }

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
        cosmosdb_postgresql,
        cosmosdb_nosql,
        mssql,
        mysql,
        postgresql
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config;

public record DataSource(DatabaseType DatabaseType, string ConnectionString, Dictionary<string, JsonElement> Options)
{
    public TOptionType? GetTypedOptions<TOptionType>() where TOptionType : IDataSourceOptions
    {
        if (typeof(TOptionType).IsAssignableFrom(typeof(CosmosDbNoSQLDataSourceOptions)))
        {
            return (TOptionType)(object)new CosmosDbNoSQLDataSourceOptions(
                    Database: ReadStringOption("database"),
                    Container: ReadStringOption("container"),
                    GraphQLSchemaPath: ReadStringOption("schema"),
                    // The "raw" schema will be provided via the controller to setup config, rather than parsed from the JSON file.
                    GraphQLSchema: ReadStringOption(CosmosDbNoSQLDataSourceOptions.GRAPHQL_RAW_KEY));
        }

        if (typeof(TOptionType).IsAssignableFrom(typeof(MsSqlOptions)))
        {
            return (TOptionType)(object)new MsSqlOptions(SetSessionContext: ReadBoolOption("set-session-context"));
        }

        throw new NotImplementedException();
    }

    private string? ReadStringOption(string option) => Options.ContainsKey(option) ? Options[option].GetString() : null;
    private bool ReadBoolOption(string option) => Options.ContainsKey(option) ? Options[option].GetBoolean() : false;

    [JsonIgnore]
    public string DatabaseTypeNotSupportedMessage => $"The provided database-type value: {DatabaseType} is currently not supported. Please check the configuration file.";
}

public interface IDataSourceOptions { }
public record CosmosDbNoSQLDataSourceOptions(string? Database, string? Container, string? GraphQLSchemaPath, string? GraphQLSchema) : IDataSourceOptions
{
    public static string GRAPHQL_RAW_KEY = "graphql-raw";
}

public record MsSqlOptions(bool SetSessionContext = true) : IDataSourceOptions;

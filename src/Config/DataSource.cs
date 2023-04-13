using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config;

public record DataSource(DatabaseType DatabaseType, string ConnectionString, Dictionary<string, JsonElement> Options)
{
    public TOptionType? GetTypedOptions<TOptionType>() where TOptionType : IDataSourceOptions
    {
        if (typeof(TOptionType).IsAssignableFrom(typeof(CosmosDbDataSourceOptions)))
        {
            return (TOptionType)(object)new CosmosDbDataSourceOptions(
                    Database: ReadOption("database"),
                    Container: ReadOption("container"),
                    GraphQLSchemaPath: ReadOption("schema"),
                    // The "raw" schema will be provided via the controller to setup config, rather than parsed from the JSON file.
                    GraphQLSchema: ReadOption(CosmosDbDataSourceOptions.GRAPHQL_RAW_KEY));
        }

        throw new NotImplementedException();
    }

    private string? ReadOption(string option) => Options.ContainsKey(option) ? Options[option].GetString() : null;

    [JsonIgnore]
    public string DatabaseTypeNotSupportedMessage => $"The provided database-type value: {DatabaseType} is currently not supported. Please check the configuration file.";
}

public interface IDataSourceOptions { }
public record CosmosDbDataSourceOptions(string? Database, string? Container, string? GraphQLSchemaPath, string? GraphQLSchema) : IDataSourceOptions
{
    public static string GRAPHQL_RAW_KEY = "graphql-raw";
}

public record MsSqlOptions(bool SetSessionContext = true) : IDataSourceOptions;

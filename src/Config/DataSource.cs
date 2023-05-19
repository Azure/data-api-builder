using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.NamingPolicies;

namespace Azure.DataApiBuilder.Config;

public record DataSource(DatabaseType DatabaseType, string ConnectionString, Dictionary<string, JsonElement> Options)
{
    public TOptionType? GetTypedOptions<TOptionType>() where TOptionType : IDataSourceOptions
    {
        HyphenatedNamingPolicy namingPolicy = new();

        if (typeof(TOptionType).IsAssignableFrom(typeof(CosmosDbNoSQLDataSourceOptions)))
        {
            return (TOptionType)(object)new CosmosDbNoSQLDataSourceOptions(
                    Database: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database))),
                    Container: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container))),
                    Schema: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema))),
                    // The "raw" schema will be provided via the controller to setup config, rather than parsed from the JSON file.
                    GraphQLSchema: ReadStringOption(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.GraphQLSchema))));
        }

        if (typeof(TOptionType).IsAssignableFrom(typeof(MsSqlOptions)))
        {
            return (TOptionType)(object)new MsSqlOptions(
                SetSessionContext: ReadBoolOption(namingPolicy.ConvertName(nameof(MsSqlOptions.SetSessionContext))));
        }

        throw new NotImplementedException();
    }

    private string? ReadStringOption(string option) => Options.ContainsKey(option) ? Options[option].GetString() : null;
    private bool ReadBoolOption(string option) => Options.ContainsKey(option) ? Options[option].GetBoolean() : false;

    [JsonIgnore]
    public string DatabaseTypeNotSupportedMessage => $"The provided database-type value: {DatabaseType} is currently not supported. Please check the configuration file.";
}

public interface IDataSourceOptions { }

/// <summary>
/// The CosmosDB NoSQL connection options.
/// </summary>
/// <param name="Database">Name of the default CosmosDB database.</param>
/// <param name="Container">Name of the default CosmosDB container.</param>
/// <param name="Schema">Path to the GraphQL schema file.</param>
/// <param name="GraphQLSchema">Raw contents of the GraphQL schema.</param>
public record CosmosDbNoSQLDataSourceOptions(string? Database, string? Container, string? Schema, string? GraphQLSchema) : IDataSourceOptions;

public record MsSqlOptions(bool SetSessionContext = true) : IDataSourceOptions;

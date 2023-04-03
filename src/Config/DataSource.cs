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
                    Database: Options["database"].GetString(),
                    Container: Options["container"].GetString(),
                    Schema: Options["schema"].GetString());
        }

        throw new NotImplementedException();
    }

    [JsonIgnore]
    public string DatabaseTypeNotSupportedMessage => $"The provided database-type value: {DatabaseType} is currently not supported. Please check the configuration file.";
}

public interface IDataSourceOptions { }
public record CosmosDbDataSourceOptions(string? Database, string? Container, string? Schema) : IDataSourceOptions;

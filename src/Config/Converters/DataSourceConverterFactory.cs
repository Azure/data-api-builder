// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class DataSourceConverterFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(DataSource));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new DataSourceConverter(_replaceEnvVar);
    }

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    internal DataSourceConverterFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class DataSourceConverter : JsonConverter<DataSource>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        public DataSourceConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        public override DataSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            DataSource dataSource = new(DatabaseType.MSSQL, string.Empty, null);
            if (reader.TokenType is JsonTokenType.StartObject)
            {

                while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
                {
                    if (reader.TokenType is JsonTokenType.PropertyName)
                    {
                        string propertyName = reader.GetString() ?? string.Empty;
                        reader.Read();
                        switch (propertyName)
                        {
                            case "database-type":
                                dataSource = dataSource with { DatabaseType = EnumExtensions.Deserialize<DatabaseType>(reader.DeserializeString(_replaceEnvVar)!) };
                                break;
                            case "connection-string":
                                dataSource = dataSource with { ConnectionString = reader.DeserializeString(replaceEnvVar: _replaceEnvVar)! };
                                break;
                            case "options":
                                if (reader.TokenType == JsonTokenType.Null)
                                {
                                    dataSource = dataSource with { Options = null };
                                    break;
                                }
                                else
                                {
                                    Dictionary<string, JsonElement> optionsDict = new();
                                    while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
                                    {
                                        string key = reader.GetString()!;
                                        reader.Read();
                                        JsonElement element;
                                        if (reader.TokenType is JsonTokenType.String)
                                        {
                                            string stringValue = reader.DeserializeString(replaceEnvVar: _replaceEnvVar)!;

                                            if (bool.TryParse(stringValue, out bool boolValue))
                                            {
                                                // MsSqlOptions.SetSessionContext will contain a boolean value.
                                                using (JsonDocument doc = JsonDocument.Parse(boolValue.ToString().ToLower()))
                                                {
                                                    element = doc.RootElement.Clone();
                                                }
                                            }
                                            else
                                            {
                                                // CosmosDbNoSQLDataSourceOptions will contain string values.
                                                using (JsonDocument doc = JsonDocument.Parse($"\"{stringValue}\""))
                                                {
                                                    element = doc.RootElement.Clone();
                                                }
                                            }
                                        }
                                        else if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                                        {
                                            using (JsonDocument doc = JsonDocument.Parse(reader.GetBoolean().ToString().ToLower()))
                                            {
                                                element = doc.RootElement.Clone();
                                            }
                                        }
                                        else if (reader.TokenType is JsonTokenType.Null)
                                        {
                                            using (JsonDocument doc = JsonDocument.Parse("null"))
                                            {
                                                element = doc.RootElement.Clone();
                                            }
                                        }
                                        else
                                        {
                                            throw new JsonException($"Unexpected value for options {key} while deserializing DataSource options.");
                                        }

                                        optionsDict.Add(key, element);
                                    }

                                    dataSource = dataSource with { Options = optionsDict };
                                    break;
                                }
                            default:
                                throw new JsonException($"Unexpected property {propertyName} while deserializing DataSource.");
                        }
                    }
                }

            }

            return dataSource;
        }

        public override void Write(Utf8JsonWriter writer, DataSource value, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is DataSourceConverterFactory));

            JsonSerializer.Serialize(writer, value, innerOptions);
        }
    }
}

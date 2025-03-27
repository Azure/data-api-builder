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
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                DatabaseType databaseType = DatabaseType.MSSQL;
                string connectionString = string.Empty;
                DatasourceHealthCheckConfig? health = null;
                Dictionary<string, object?>? datasourceOptions = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new DataSource(databaseType, connectionString, datasourceOptions, health);
                    }

                    if (reader.TokenType is JsonTokenType.PropertyName)
                    {
                        string propertyName = reader.GetString() ?? string.Empty;
                        reader.Read();

                        switch (propertyName)
                        {
                            case "database-type":
                                databaseType = EnumExtensions.Deserialize<DatabaseType>(reader.DeserializeString(_replaceEnvVar)!);
                                break;

                            case "connection-string":
                                connectionString = reader.DeserializeString(replaceEnvVar: _replaceEnvVar)!;
                                break;

                            case "health":
                                if (reader.TokenType == JsonTokenType.Null)
                                {
                                    health = new();
                                }
                                else
                                {
                                    try
                                    {
                                        health = JsonSerializer.Deserialize<DatasourceHealthCheckConfig>(ref reader, options);
                                    }
                                    catch (Exception e)
                                    {
                                        throw new JsonException($"Error while deserializing DataSource health: {e.Message}");
                                    }
                                }

                                break;
                            case "options":
                                if (reader.TokenType is not JsonTokenType.Null)
                                {
                                    Dictionary<string, object?> optionsDict = new();
                                    while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
                                    {
                                        string optionsSubproperty = reader.GetString()!;
                                        reader.Read();
                                        object? optionsSubpropertyValue;
                                        if (reader.TokenType is JsonTokenType.String)
                                        {
                                            // Determine whether to resolve the environment variable or keep as-is.
                                            string stringValue = reader.DeserializeString(replaceEnvVar: _replaceEnvVar)!;

                                            if (bool.TryParse(stringValue, out bool boolValue))
                                            {
                                                // MsSqlOptions.SetSessionContext will contain a boolean value.
                                                optionsSubpropertyValue = boolValue;
                                            }
                                            else
                                            {
                                                // CosmosDbNoSQLDataSourceOptions will contain string values.
                                                optionsSubpropertyValue = stringValue;
                                            }
                                        }
                                        else if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                                        {
                                            optionsSubpropertyValue = reader.GetBoolean();
                                        }
                                        else if (reader.TokenType is JsonTokenType.Null)
                                        {
                                            optionsSubpropertyValue = null;
                                        }
                                        else
                                        {
                                            throw new JsonException($"Unexpected value for options {optionsSubproperty} while deserializing DataSource options.");
                                        }

                                        optionsDict.Add(optionsSubproperty, optionsSubpropertyValue);
                                    }

                                    datasourceOptions = optionsDict;
                                }

                                break;
                            default:
                                throw new JsonException($"Unexpected property {propertyName} while deserializing DataSource.");
                        }
                    }
                }
            }

            throw new JsonException("data-source property has a missing }.");
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

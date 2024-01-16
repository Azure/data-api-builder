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
                
                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType is JsonTokenType.PropertyName)
                    {
                        string propertyName = reader.GetString() ?? string.Empty;
                        // reader.Read();
                        switch (propertyName)
                        {
                            case "database-type":
                                dataSource = dataSource with { DatabaseType = EnumExtensions.Deserialize<DatabaseType>(reader.DeserializeString(_replaceEnvVar)!) };
                                break;
                            case "connection-string":
                                dataSource = dataSource with { ConnectionString = reader.DeserializeString(replaceEnvVar: true) ?? string.Empty };
                                break;
                            case "options":
                                dataSource = dataSource with { Options = JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(ref reader, options) };
                                break;
                            default:
                                throw new JsonException($"Unexpected property {propertyName} while deserializing DataSource.");
                        }
                    }
                }

                
            }

            return dataSource;
        }

        // private static Dictionary<string, JsonElement>? DeserializeDataSourceOptions(ref Utf8JsonReader reader){
        //     Dictionary<string,JsonElement> dict = new();
        //     if (reader.TokenType is JsonTokenType.StartObject)
        //     {
        //         while (reader.Read())
        //         {
        //             if (reader.TokenType is JsonTokenType.EndObject)
        //             {
        //                 break;
        //             }

        //             if (reader.TokenType is JsonTokenType.PropertyName)
        //             {
        //                 string key= reader.GetString()!;
        //                 reader.Read();
        //                 if (reader.TokenType is JsonTokenType.String)
        //                 {
        //                     string value = reader.DeserializeString(replaceEnvVar: true) ?? string.Empty;
        //                     using (JsonDocument doc = JsonDocument.Parse($"\"{value}\""))
        //                     {
        //                         dict.Add(key, doc.RootElement);
        //                     }
        //                 }
        //                 else if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False){
        //                     bool value = reader.GetBoolean();
        //                     using (JsonDocument doc = JsonDocument.Parse(value.ToString().ToLower()))
        //                     {
        //                         dict.Add(key, doc.RootElement);
        //                     }
        //                 }
        //                 else {
        //                     throw new JsonException($"Unexpected value Type {reader.TokenType} for Key {key} while deserializing DataSource Options.");
        //                 }
        //             }
        //         }

        //         return dict;
        //     }

        //     return null;
        // }

        public override void Write(Utf8JsonWriter writer, DataSource value, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is DataSourceConverterFactory));

            JsonSerializer.Serialize(writer, value, innerOptions);
        }
    }
}

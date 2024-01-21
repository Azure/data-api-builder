// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters
{
    /// <summary>
    /// Converter for the nested insert operation options.
    /// </summary>
    internal class NestedInsertOptionsConverter : JsonConverter<NestedInsertOptions>
    {
        /// <inheritdoc/>
        public override NestedInsertOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                NestedInsertOptions? nestedInsertOptions = new(enabled: false);
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string? propertyName = reader.GetString();
                    switch (propertyName)
                    {
                        case "enabled":
                            reader.Read();
                            if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
                            {
                                nestedInsertOptions = new(reader.GetBoolean());
                            }

                            break;
                        default:
                            throw new JsonException($"Unexpected property {propertyName}");

                    }
                }

                return nestedInsertOptions;
            }

            throw new JsonException();
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, NestedInsertOptions value, JsonSerializerOptions options)
        {
            writer.WritePropertyName("inserts");

            writer.WriteStartObject();
            writer.WritePropertyName("enabled");
            writer.WriteBooleanValue(value.Enabled);
            writer.WriteEndObject();
        }
    }
}

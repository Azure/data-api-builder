// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters
{
    /// <summary>
    /// Converter for the nested create operation options.
    /// </summary>
    internal class NestedCreateOptionsConverter : JsonConverter<NestedCreateOptions>
    {
        /// <inheritdoc/>
        public override NestedCreateOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                NestedCreateOptions? nestedCreateOptions = null;
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
                                nestedCreateOptions = new(reader.GetBoolean());
                            }

                            break;
                        default:
                            throw new JsonException($"Unexpected property {propertyName}");

                    }
                }

                return nestedCreateOptions;
            }

            throw new JsonException();
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, NestedCreateOptions? value, JsonSerializerOptions options)
        {
            // If the value is null, it is not written to the config file.
            if (value is null)
            {
                return;
            }

            writer.WritePropertyName("create");

            writer.WriteStartObject();
            writer.WritePropertyName("enabled");
            writer.WriteBooleanValue(value.Enabled);
            writer.WriteEndObject();
        }
    }
}

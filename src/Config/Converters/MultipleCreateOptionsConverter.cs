// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters
{
    /// <summary>
    /// Converter for the multiple create operation options.
    /// </summary>
    internal class MultipleCreateOptionsConverter : JsonConverter<MultipleCreateOptions>
    {
        /// <inheritdoc/>
        public override MultipleCreateOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                MultipleCreateOptions? multipleCreateOptions = null;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string? propertyName = reader.GetString();

                    if (propertyName is null)
                    {
                        throw new JsonException("Invalid property : null");
                    }

                    switch (propertyName)
                    {
                        case "enabled":
                            reader.Read();
                            if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
                            {
                                multipleCreateOptions = new(reader.GetBoolean());
                            }

                            break;
                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }

                return multipleCreateOptions;
            }

            throw new JsonException("Failed to read the GraphQL Multiple Create options");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, MultipleCreateOptions? value, JsonSerializerOptions options)
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

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Factory for creating EntityMcpOptions converters.
/// </summary>
internal class EntityMcpOptionsConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(EntityMcpOptions);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityMcpOptionsConverter();
    }

    /// <summary>
    /// Converter for EntityMcpOptions that handles both boolean and object representations.
    /// When boolean: true enables dml-tools and custom-tool remains false (default), false disables dml-tools and custom-tool remains false.
    /// When object: can specify individual properties (custom-tool and dml-tools).
    /// </summary>
    private class EntityMcpOptionsConverter : JsonConverter<EntityMcpOptions>
    {
        public override EntityMcpOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            // Handle boolean shorthand: true/false
            if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
            {
                bool value = reader.GetBoolean();
                // Boolean true means: dml-tools=true, custom-tool=false (default)
                // Boolean false means: dml-tools=false, custom-tool=false
                // Pass null for customToolEnabled to keep it as default (not user-provided)
                return new EntityMcpOptions(
                    customToolEnabled: null,
                    dmlToolsEnabled: value
                );
            }

            // Handle object representation
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                bool? customToolEnabled = null;
                bool? dmlToolsEnabled = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string? propertyName = reader.GetString();
                        reader.Read(); // Move to the value

                        switch (propertyName)
                        {
                            case "custom-tool":
                                customToolEnabled = reader.TokenType == JsonTokenType.True;
                                break;
                            case "dml-tools":
                                dmlToolsEnabled = reader.TokenType == JsonTokenType.True;
                                break;
                            default:
                                throw new JsonException($"Unknown property '{propertyName}' in EntityMcpOptions");
                        }
                    }
                }

                return new EntityMcpOptions(customToolEnabled, dmlToolsEnabled);
            }

            throw new JsonException($"Unexpected token type {reader.TokenType} for EntityMcpOptions");
        }

        public override void Write(Utf8JsonWriter writer, EntityMcpOptions value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                return;
            }

            // Check if we should write as boolean shorthand
            // Write as boolean if: only dml-tools is set (or custom-tool is default false)
            bool writeAsBoolean = !value.UserProvidedCustomToolEnabled && value.UserProvidedDmlToolsEnabled;

            if (writeAsBoolean)
            {
                // Write as boolean shorthand
                writer.WriteBooleanValue(value.DmlToolEnabled);
            }
            else if (value.UserProvidedCustomToolEnabled || value.UserProvidedDmlToolsEnabled)
            {
                // Write as object
                writer.WriteStartObject();

                if (value.UserProvidedCustomToolEnabled)
                {
                    writer.WriteBoolean("custom-tool", value.CustomToolEnabled);
                }

                if (value.UserProvidedDmlToolsEnabled)
                {
                    writer.WriteBoolean("dml-tools", value.DmlToolEnabled);
                }

                writer.WriteEndObject();
            }
        }
    }
}

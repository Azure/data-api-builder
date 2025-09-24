// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// JSON converter factory for McpRuntimeOptions that handles both boolean and object formats.
/// </summary>
internal class McpRuntimeOptionsConverterFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(McpRuntimeOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new McpRuntimeOptionsConverter(_replaceEnvVar);
    }

    internal McpRuntimeOptionsConverterFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class McpRuntimeOptionsConverter : JsonConverter<McpRuntimeOptions>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        internal McpRuntimeOptionsConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        /// <summary>
        /// Defines how DAB reads MCP options and defines which values are
        /// used to instantiate McpRuntimeOptions.
        /// </summary>
        /// <exception cref="JsonException">Thrown when improperly formatted MCP options are provided.</exception>
        public override McpRuntimeOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
            {
                return new McpRuntimeOptions(Enabled: reader.GetBoolean());
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                DmlToolsConfigConverter dmlToolsConfigConverter = new();

                bool enabled = true;
                string? path = null;
                DmlToolsConfig? dmlTools = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return new McpRuntimeOptions(enabled, path, dmlTools);
                    }

                    string? propertyName = reader.GetString();

                    reader.Read();
                    switch (propertyName)
                    {
                        case "enabled":
                            if (reader.TokenType is not JsonTokenType.Null)
                            {
                                enabled = reader.GetBoolean();
                            }

                            break;

                        case "path":
                            if (reader.TokenType is not JsonTokenType.Null)
                            {
                                path = reader.DeserializeString(_replaceEnvVar);
                            }

                            break;

                        case "dml-tools":
                            dmlTools = dmlToolsConfigConverter.Read(ref reader, typeToConvert, options);
                            break;

                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }
            }

            throw new JsonException("Failed to read the MCP Options");
        }

        /// <summary>
        /// When writing the McpRuntimeOptions back to a JSON file, only write the properties
        /// if they are user provided. This avoids polluting the written JSON file with properties
        /// the user most likely omitted when writing the original DAB runtime config file.
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, McpRuntimeOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value.Enabled);

            if (value?.UserProvidedPath is true)
            {
                writer.WritePropertyName("path");
                JsonSerializer.Serialize(writer, value.Path, options);
            }

            // Only write the boolean value if it's not the default (true)
            // This prevents writing "dml-tools": true when it's the default
            if (value?.DmlTools is not null)
            {
                DmlToolsConfigConverter dmlToolsOptionsConverter = options.GetConverter(typeof(DmlToolsConfig)) as DmlToolsConfigConverter ??
                                    throw new JsonException("Failed to get mcp.dml-tools options converter");

                dmlToolsOptionsConverter.Write(writer, value.DmlTools, options);
            }

            writer.WriteEndObject();
        }
    }
}

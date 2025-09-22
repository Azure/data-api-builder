// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// JSON converter factory for DmlToolsConfig that handles both boolean and object formats.
/// </summary>
internal class McpOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(DmlToolsConfig));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new DmlToolsConfigConverter();
    }

    private class DmlToolsConfigConverter : JsonConverter<DmlToolsConfig>
    {
        /// <summary>
        /// Reads DmlToolsConfig from JSON which can be either:
        /// - A boolean: all tools are enabled/disabled
        /// - An object: individual tool settings
        /// </summary>
        public override DmlToolsConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Null)
            {
                return null;
            }

            // Handle boolean format: "dml-tools": true/false
            if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
            {
                bool enabled = reader.GetBoolean();
                return DmlToolsConfig.FromBoolean(enabled);
            }

            // Handle object format
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                bool? describeEntities = null;
                bool? createEntity = null;
                bool? readEntity = null;
                bool? updateEntity = null;
                bool? deleteEntity = null;
                bool? executeEntity = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new DmlToolsConfig
                        {
                            AllToolsEnabled = false, // Default when using object format
                            DescribeEntities = describeEntities,
                            CreateEntity = createEntity,
                            ReadEntity = readEntity,
                            UpdateEntity = updateEntity,
                            DeleteEntity = deleteEntity,
                            ExecuteEntity = executeEntity
                        };
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "describe-entities":
                            describeEntities = reader.GetBoolean();
                            break;
                        case "create-entity":
                            createEntity = reader.GetBoolean();
                            break;
                        case "read-entity":
                            readEntity = reader.GetBoolean();
                            break;
                        case "update-entity":
                            updateEntity = reader.GetBoolean();
                            break;
                        case "delete-entity":
                            deleteEntity = reader.GetBoolean();
                            break;
                        case "execute-entity":
                            executeEntity = reader.GetBoolean();
                            break;
                        default:
                            throw new JsonException($"Unexpected property '{property}' in dml-tools configuration.");
                    }
                }
            }

            throw new JsonException("DML Tools configuration is missing closing brace.");
        }

        /// <summary>
        /// Writes DmlToolsConfig to JSON.
        /// If all tools have the same state, writes as boolean.
        /// Otherwise, writes as an object with individual tool settings.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, DmlToolsConfig? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            // Check if this can be simplified to a boolean
            bool hasIndividualSettings = value.DescribeEntities.HasValue ||
                                       value.CreateEntity.HasValue ||
                                       value.ReadEntity.HasValue ||
                                       value.UpdateEntity.HasValue ||
                                       value.DeleteEntity.HasValue ||
                                       value.ExecuteEntity.HasValue;

            if (!hasIndividualSettings)
            {
                writer.WriteBooleanValue(value.AllToolsEnabled);
            }
            else
            {
                writer.WriteStartObject();

                if (value.DescribeEntities.HasValue)
                {
                    writer.WriteBoolean("describe-entities", value.DescribeEntities.Value);
                }

                if (value.CreateEntity.HasValue)
                {
                    writer.WriteBoolean("create-entity", value.CreateEntity.Value);
                }

                if (value.ReadEntity.HasValue)
                {
                    writer.WriteBoolean("read-entity", value.ReadEntity.Value);
                }

                if (value.UpdateEntity.HasValue)
                {
                    writer.WriteBoolean("update-entity", value.UpdateEntity.Value);
                }

                if (value.DeleteEntity.HasValue)
                {
                    writer.WriteBoolean("delete-entity", value.DeleteEntity.Value);
                }

                if (value.ExecuteEntity.HasValue)
                {
                    writer.WriteBoolean("execute-entity", value.ExecuteEntity.Value);
                }

                writer.WriteEndObject();
            }
        }
    }
}

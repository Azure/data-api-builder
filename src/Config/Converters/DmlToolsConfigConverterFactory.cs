// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// JSON converter factory for DmlToolsConfig that handles both boolean and object formats.
/// </summary>
internal class DmlToolsConfigConverterFactory : JsonConverterFactory
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
                bool? createRecord = null;
                bool? readRecords = null;
                bool? updateRecord = null;
                bool? deleteRecord = null;
                bool? executeRecord = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new DmlToolsConfig
                        {
                            AllToolsEnabled = false, // Default when using object format
                            DescribeEntities = describeEntities,
                            CreateRecord = createRecord,
                            ReadRecords = readRecords,
                            UpdateRecord = updateRecord,
                            DeleteRecord = deleteRecord,
                            ExecuteRecord = executeRecord
                        };
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "describe-entities":
                            describeEntities = reader.GetBoolean();
                            break;
                        case "create-record":
                            createRecord = reader.GetBoolean();
                            break;
                        case "read-records":
                            readRecords = reader.GetBoolean();
                            break;
                        case "update-record":
                            updateRecord = reader.GetBoolean();
                            break;
                        case "delete-record":
                            deleteRecord = reader.GetBoolean();
                            break;
                        case "execute-record":
                            executeRecord = reader.GetBoolean();
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
                return;
            }

            // Check if this can be simplified to a boolean
            bool hasIndividualSettings = value.DescribeEntities.HasValue ||
                                       value.CreateRecord.HasValue ||
                                       value.ReadRecords.HasValue ||
                                       value.UpdateRecord.HasValue ||
                                       value.DeleteRecord.HasValue ||
                                       value.ExecuteRecord.HasValue;

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

                if (value.CreateRecord.HasValue)
                {
                    writer.WriteBoolean("create-record", value.CreateRecord.Value);
                }

                if (value.ReadRecords.HasValue)
                {
                    writer.WriteBoolean("read-records", value.ReadRecords.Value);
                }

                if (value.UpdateRecord.HasValue)
                {
                    writer.WriteBoolean("update-record", value.UpdateRecord.Value);
                }

                if (value.DeleteRecord.HasValue)
                {
                    writer.WriteBoolean("delete-record", value.DeleteRecord.Value);
                }

                if (value.ExecuteRecord.HasValue)
                {
                    writer.WriteBoolean("execute-record", value.ExecuteRecord.Value);
                }

                writer.WriteEndObject();
            }
        }
    }
}

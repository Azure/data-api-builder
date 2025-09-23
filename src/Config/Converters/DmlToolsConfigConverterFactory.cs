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
        /// - Null/undefined: defaults to all tools enabled (true)
        /// </summary>
        public override DmlToolsConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Null)
            {
                // Return default config with all tools enabled
                return DmlToolsConfig.Default;
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
                bool? readRecord = null;
                bool? updateRecord = null;
                bool? deleteRecord = null;
                bool? executeRecord = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new DmlToolsConfig(
                            allToolsEnabled: null,
                            describeEntities: describeEntities,
                            createRecord: createRecord,
                            readRecords: readRecord,
                            updateRecord: updateRecord,
                            deleteRecord: deleteRecord,
                            executeRecord: executeRecord);
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
                            readRecord = reader.GetBoolean();
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
        /// Only writes user-provided properties to avoid bloating the config file.
        /// If no individual settings provided, writes as boolean.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, DmlToolsConfig? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                // Don't write null - omit the property entirely
                return;
            }

            // Check if any individual settings were provided by the user
            bool hasIndividualSettings = value.UserProvidedDescribeEntities ||
                                       value.UserProvidedCreateRecord ||
                                       value.UserProvidedReadRecords ||
                                       value.UserProvidedUpdateRecord ||
                                       value.UserProvidedDeleteRecord ||
                                       value.UserProvidedExecuteRecord;

            if (!hasIndividualSettings)
            {
                // Only write the boolean value if it's not the default (true)
                // This prevents writing "dml-tools": true when it's the default
                if (value.AllToolsEnabled != DmlToolsConfig.DEFAULT_ENABLED)
                {
                    writer.WriteBooleanValue(value.AllToolsEnabled);
                }
                // Otherwise, don't write anything (property will be omitted)
            }
            else
            {
                // Write as object with only user-provided properties
                writer.WriteStartObject();

                if (value.UserProvidedDescribeEntities)
                {
                    writer.WriteBoolean("describe-entities", value.DescribeEntities.Value);
                }

                if (value.UserProvidedCreateRecord)
                {
                    writer.WriteBoolean("create-record", value.CreateRecord.Value);
                }

                if (value.UserProvidedReadRecords)
                {
                    writer.WriteBoolean("read-records", value.ReadRecords.Value);
                }

                if (value.UserProvidedUpdateRecord)
                {
                    writer.WriteBoolean("update-record", value.UpdateRecord.Value);
                }

                if (value.UserProvidedDeleteRecord)
                {
                    writer.WriteBoolean("delete-record", value.DeleteRecord.Value);
                }

                if (value.UserProvidedExecuteRecord)
                {
                    writer.WriteBoolean("execute-record", value.ExecuteRecord.Value);
                }

                writer.WriteEndObject();
            }
        }
    }
}

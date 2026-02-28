// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// JSON converter for DmlToolsConfig that handles both boolean and object formats.
/// </summary>
internal class DmlToolsConfigConverter : JsonConverter<DmlToolsConfig>
{
    /// <summary>
    /// Reads DmlToolsConfig from JSON which can be either:
    /// - A boolean: all tools are enabled/disabled
    /// - An object: individual tool settings (unspecified tools default to true)
    /// - Null/undefined: defaults to all tools enabled (true)
    /// </summary>
    public override DmlToolsConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Handle null
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
            // Start with null values - only set when explicitly provided in JSON
            bool? describeEntities = null;
            bool? createRecord = null;
            bool? readRecords = null;
            bool? updateRecord = null;
            bool? deleteRecord = null;
            bool? executeEntity = null;
            bool? aggregateRecords = null;

            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType is JsonTokenType.PropertyName)
                {
                    string? property = reader.GetString();
                    reader.Read();

                    // Handle the property value
                    if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
                    {
                        bool value = reader.GetBoolean();

                        switch (property?.ToLowerInvariant())
                        {
                            case "describe-entities":
                                describeEntities = value;
                                break;
                            case "create-record":
                                createRecord = value;
                                break;
                            case "read-records":
                                readRecords = value;
                                break;
                            case "update-record":
                                updateRecord = value;
                                break;
                            case "delete-record":
                                deleteRecord = value;
                                break;
                            case "execute-entity":
                                executeEntity = value;
                                break;
                            case "aggregate-records":
                                aggregateRecords = value;
                                break;
                            default:
                                // Skip unknown properties
                                break;
                        }
                    }
                    else
                    {
                        // Error on non-boolean values for known properties
                        if (property?.ToLowerInvariant() is "describe-entities" or "create-record"
                            or "read-records" or "update-record" or "delete-record" or "execute-entity"
                            or "aggregate-records")
                        {
                            throw new JsonException($"Property '{property}' must be a boolean value.");
                        }

                        // Skip unknown properties
                        reader.Skip();
                    }
                }
            }

            // Pass null for unspecified values - the constructor will handle defaults
            return new DmlToolsConfig(
                allToolsEnabled: null,
                describeEntities: describeEntities,
                createRecord: createRecord,
                readRecords: readRecords,
                updateRecord: updateRecord,
                deleteRecord: deleteRecord,
                executeEntity: executeEntity,
                aggregateRecords: aggregateRecords);
        }

        // For any other unexpected token type, return default (all enabled)
        return DmlToolsConfig.Default;
    }

    /// <summary>
    /// Writes DmlToolsConfig to JSON.
    /// - If all tools have the same value, writes as boolean
    /// - Otherwise writes as object with only user-provided properties
    /// </summary>
    public override void Write(Utf8JsonWriter writer, DmlToolsConfig? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            return;
        }

        // Check if any individual settings were provided by the user
        bool hasIndividualSettings = value.UserProvidedDescribeEntities ||
                                    value.UserProvidedCreateRecord ||
                                    value.UserProvidedReadRecords ||
                                    value.UserProvidedUpdateRecord ||
                                    value.UserProvidedDeleteRecord ||
                                    value.UserProvidedExecuteEntity ||
                                    value.UserProvidedAggregateRecords;

        // Only write the boolean value if it's provided by user
        // This prevents writing "dml-tools": true when it's the default
        if (!hasIndividualSettings && value.UserProvidedAllTools)
        {
            writer.WritePropertyName("dml-tools");
            writer.WriteBooleanValue(value.AllToolsEnabled);
        }
        else if (hasIndividualSettings)
        {
            writer.WritePropertyName("dml-tools");

            // Write as object with only user-provided properties
            writer.WriteStartObject();

            if (value.UserProvidedDescribeEntities && value.DescribeEntities.HasValue)
            {
                writer.WriteBoolean("describe-entities", value.DescribeEntities.Value);
            }

            if (value.UserProvidedCreateRecord && value.CreateRecord.HasValue)
            {
                writer.WriteBoolean("create-record", value.CreateRecord.Value);
            }

            if (value.UserProvidedReadRecords && value.ReadRecords.HasValue)
            {
                writer.WriteBoolean("read-records", value.ReadRecords.Value);
            }

            if (value.UserProvidedUpdateRecord && value.UpdateRecord.HasValue)
            {
                writer.WriteBoolean("update-record", value.UpdateRecord.Value);
            }

            if (value.UserProvidedDeleteRecord && value.DeleteRecord.HasValue)
            {
                writer.WriteBoolean("delete-record", value.DeleteRecord.Value);
            }

            if (value.UserProvidedExecuteEntity && value.ExecuteEntity.HasValue)
            {
                writer.WriteBoolean("execute-entity", value.ExecuteEntity.Value);
            }

            if (value.UserProvidedAggregateRecords && value.AggregateRecords.HasValue)
            {
                writer.WriteBoolean("aggregate-records", value.AggregateRecords.Value);
            }

            writer.WriteEndObject();
        }
    }
}

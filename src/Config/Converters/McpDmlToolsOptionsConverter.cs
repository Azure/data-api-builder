// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class McpDmlToolsOptionsConverter : JsonConverter<McpDmlToolsOptions>
{
    /// <summary>
    /// Defines how DAB reads MCP DML Tools options and defines which values are
    /// used to instantiate McpDmlToolsOptions.
    /// </summary>
    /// <exception cref="JsonException">Thrown when improperly formatted MCP DML Tools options are provided.</exception>
    public override McpDmlToolsOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)
        {
            return new McpDmlToolsOptions(true, true, true, true, true, true);
        }

        if (reader.TokenType == JsonTokenType.False || reader.TokenType == JsonTokenType.Null)
        {
            return new McpDmlToolsOptions();
        }

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
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new McpDmlToolsOptions(describeEntities, createRecord, readRecord, updateRecord, deleteRecord, executeRecord);
                }

                string? propertyName = reader.GetString();

                reader.Read();
                switch (propertyName)
                {
                    case "describe-entities":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            describeEntities = reader.GetBoolean();
                        }

                        break;

                    case "create-record":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            createRecord = reader.GetBoolean();
                        }

                        break;

                    case "read-record":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            readRecord = reader.GetBoolean();
                        }

                        break;

                    case "update-record":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            updateRecord = reader.GetBoolean();
                        }

                        break;

                    case "delete-record":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            deleteRecord = reader.GetBoolean();
                        }

                        break;

                    case "execute-record":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            executeRecord = reader.GetBoolean();
                        }

                        break;

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }
        }

        throw new JsonException("Failed to read the MCP DML Tools Options");
    }

    /// <summary>
    /// When writing the McpDmlToolsOptions back to a JSON file, only write the properties
    /// if they are user provided. This avoids polluting the written JSON file with properties
    /// the user most likely omitted when writing the original DAB runtime config file.
    /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, McpDmlToolsOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value?.UserProvidedDescribeEntities is true)
        {
            writer.WritePropertyName("describe-entities");
            JsonSerializer.Serialize(writer, value.DescribeEntities, options);
        }

        if (value?.UserProvidedCreateRecord is true)
        {
            writer.WritePropertyName("create-record");
            JsonSerializer.Serialize(writer, value.CreateRecord, options);
        }

        if (value?.UserProvidedReadRecord is true)
        {
            writer.WritePropertyName("read-record");
            JsonSerializer.Serialize(writer, value.ReadRecord, options);
        }

        if (value?.UserProvidedUpdateRecord is true)
        {
            writer.WritePropertyName("update-record");
            JsonSerializer.Serialize(writer, value.UpdateRecord, options);
        }

        if (value?.UserProvidedDeleteRecord is true)
        {
            writer.WritePropertyName("delete-record");
            JsonSerializer.Serialize(writer, value.DeleteRecord, options);
        }

        if (value?.UserProvidedExecuteRecord is true)
        {
            writer.WritePropertyName("execute-record");
            JsonSerializer.Serialize(writer, value.ExecuteRecord, options);
        }
    }
}

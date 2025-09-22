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
            bool? createEntity = null;
            bool? readEntity = null;
            bool? updateEntity = null;
            bool? deleteEntity = null;
            bool? executeEntity = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new McpDmlToolsOptions(describeEntities, createEntity, readEntity, updateEntity, deleteEntity, executeEntity);
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

                    case "create-entity":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            createEntity = reader.GetBoolean();
                        }

                        break;

                    case "read-entity":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            readEntity = reader.GetBoolean();
                        }

                        break;

                    case "update-entity":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            updateEntity = reader.GetBoolean();
                        }

                        break;

                    case "delete-entity":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            deleteEntity = reader.GetBoolean();
                        }

                        break;

                    case "execute-entity":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            executeEntity = reader.GetBoolean();
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

        if (value?.UserProvidedCreateEntity is true)
        {
            writer.WritePropertyName("create-entity");
            JsonSerializer.Serialize(writer, value.CreateEntity, options);
        }

        if (value?.UserProvidedReadEntity is true)
        {
            writer.WritePropertyName("read-entity");
            JsonSerializer.Serialize(writer, value.ReadEntity, options);
        }

        if (value?.UserProvidedUpdateEntity is true)
        {
            writer.WritePropertyName("update-entity");
            JsonSerializer.Serialize(writer, value.UpdateEntity, options);
        }

        if (value?.UserProvidedDeleteEntity is true)
        {
            writer.WritePropertyName("delete-entity");
            JsonSerializer.Serialize(writer, value.DeleteEntity, options);
        }

        if (value?.UserProvidedExecuteEntity is true)
        {
            writer.WritePropertyName("execute-entity");
            JsonSerializer.Serialize(writer, value.ExecuteEntity, options);
        }
    }
}

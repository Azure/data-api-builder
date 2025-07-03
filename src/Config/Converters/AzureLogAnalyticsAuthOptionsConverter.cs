// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class AzureLogAnalyticsAuthOptionsConverter : JsonConverter<AzureLogAnalyticsAuthOptions>
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    public AzureLogAnalyticsAuthOptionsConverter(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    /// <summary>
    /// Defines how DAB reads Azure Log Analytics Auth options and defines which values are
    /// used to instantiate AzureLogAnalyticsAuthOptions.
    /// Uses default deserialize.
    /// </summary>
    /// <exception cref="JsonException">Thrown when improperly formatted Azure Log Analytics Auth options are provided.</exception>
    public override AzureLogAnalyticsAuthOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartObject)
        {
            AzureLogAnalyticsAuthOptions? authOptions = new();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                string? propertyName = reader.GetString();

                reader.Read();
                switch (propertyName)
                {
                    case "workspace-id":
                        if (reader.TokenType is JsonTokenType.String)
                        {
                            string? workspaceId = reader.DeserializeString(_replaceEnvVar);
                            if (workspaceId is null)
                            {
                                throw new JsonException($"Unsuported null value entered for the property workspace-id");
                            }

                            authOptions = authOptions with { WorkspaceId = workspaceId };
                        }
                        else
                        {
                            throw new JsonException($"Unexpected type of value entered for workspace-id: {reader.TokenType}");
                        }

                        break;

                    case "dcr-immutable-id":
                        if (reader.TokenType is JsonTokenType.String)
                        {
                            string? dcrImmutableId = reader.DeserializeString(_replaceEnvVar);
                            if (dcrImmutableId is null)
                            {
                                throw new JsonException($"Unsuported null value entered for the property dcr-immutable-id");
                            }

                            authOptions = authOptions with { DcrImmutableId = dcrImmutableId };
                        }
                        else
                        {
                            throw new JsonException($"Unexpected type of value entered for dcr-immutable-id: {reader.TokenType}");
                        }

                        break;

                    case "dce-endpoint":
                        if (reader.TokenType is JsonTokenType.String)
                        {
                            string? dceEndpoint = reader.DeserializeString(_replaceEnvVar);
                            if (dceEndpoint is null)
                            {
                                throw new JsonException($"Unsuported null value entered for the property dce-endpoint");
                            }

                            authOptions = authOptions with { DceEndpoint = dceEndpoint };
                        }
                        else
                        {
                            throw new JsonException($"Unexpected type of value entered for dce-endpoint: {reader.TokenType}");
                        }

                        break;

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }

            return authOptions;
        }

        throw new JsonException("Failed to read the Azure Log Analytics Auth Options");
    }

    /// <summary>
    /// When writing the AzureLogAnalyticsAuthOptions back to a JSON file, only write the properties
    /// if they are user provided. This avoids polluting the written JSON file with properties
    /// the user most likely omitted when writing the original DAB runtime config file.
    /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, AzureLogAnalyticsAuthOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value?.UserProvidedWorkspaceId is true)
        {
            writer.WritePropertyName("workspace-id");
            JsonSerializer.Serialize(writer, value.WorkspaceId, options);
        }

        if (value?.UserProvidedDcrImmutableId is true)
        {
            writer.WritePropertyName("drc-immutable-id");
            JsonSerializer.Serialize(writer, value.DcrImmutableId, options);
        }

        if (value?.UserProvidedDceEndpoint is true)
        {
            writer.WritePropertyName("dce-endpoint");
            JsonSerializer.Serialize(writer, value.DceEndpoint, options);
        }

        writer.WriteEndObject();
    }
}

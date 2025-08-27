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
    /// </summary>
    /// <exception cref="JsonException">Thrown when improperly formatted Azure Log Analytics Auth options are provided.</exception>
    public override AzureLogAnalyticsAuthOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartObject)
        {
            string? customTableName = null;
            string? dcrImmutableId = null;
            string? dceEndpoint = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new AzureLogAnalyticsAuthOptions(customTableName, dcrImmutableId, dceEndpoint);
                }

                string? propertyName = reader.GetString();

                reader.Read();
                switch (propertyName)
                {
                    case "custom-table-name":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            customTableName = reader.DeserializeString(_replaceEnvVar);
                        }

                        break;

                    case "dcr-immutable-id":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            dcrImmutableId = reader.DeserializeString(_replaceEnvVar);
                        }

                        break;

                    case "dce-endpoint":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            dceEndpoint = reader.DeserializeString(_replaceEnvVar);
                        }

                        break;

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }
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

        if (value?.UserProvidedCustomTableName is true)
        {
            writer.WritePropertyName("custom-table-name");
            JsonSerializer.Serialize(writer, value.CustomTableName, options);
        }

        if (value?.UserProvidedDcrImmutableId is true)
        {
            writer.WritePropertyName("dcr-immutable-id");
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

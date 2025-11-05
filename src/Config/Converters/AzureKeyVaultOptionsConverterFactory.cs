// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Converter factory for AzureKeyVaultOptions that can optionally perform variable replacement.
/// </summary>
internal class AzureKeyVaultOptionsConverterFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private readonly DeserializationVariableReplacementSettings? _replacementSettings;

    /// <param name="replacementSettings">How to handle variable replacement during deserialization.</param>
    internal AzureKeyVaultOptionsConverterFactory(DeserializationVariableReplacementSettings? replacementSettings = null)
    {
        _replacementSettings = replacementSettings;
    }

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(AzureKeyVaultOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new AzureKeyVaultOptionsConverter(_replacementSettings);
    }

    private class AzureKeyVaultOptionsConverter : JsonConverter<AzureKeyVaultOptions>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private readonly DeserializationVariableReplacementSettings? _replacementSettings;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        public AzureKeyVaultOptionsConverter(DeserializationVariableReplacementSettings? replacementSettings)
        {
            _replacementSettings = replacementSettings;
        }

        /// <summary>
        /// Reads AzureKeyVaultOptions with optional variable replacement.
        /// </summary>
        public override AzureKeyVaultOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                string? endpoint = null;
                AKVRetryPolicyOptions? retryPolicy = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new AzureKeyVaultOptions(endpoint, retryPolicy);
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "endpoint":
                            if (reader.TokenType is JsonTokenType.String)
                            {
                                endpoint = reader.DeserializeString(_replacementSettings);
                            }

                            break;

                        case "retry-policy":
                            if (reader.TokenType is JsonTokenType.StartObject)
                            {
                                // Uses the AKVRetryPolicyOptionsConverter to read the retry-policy object.
                                retryPolicy = JsonSerializer.Deserialize<AKVRetryPolicyOptions>(ref reader, options);
                            }

                            break;

                        default:
                            throw new JsonException($"Unexpected property {property}");
                    }
                }
            }

            throw new JsonException("Invalid AzureKeyVaultOptions format");
        }

        /// <summary>
        /// When writing the AzureKeyVaultOptions back to a JSON file, only write the properties
        /// if they are user provided. This avoids polluting the written JSON file with properties
        /// the user most likely omitted when writing the original DAB runtime config file.
        /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, AzureKeyVaultOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value?.UserProvidedEndpoint is true)
            {
                writer.WritePropertyName("endpoint");
                JsonSerializer.Serialize(writer, value.Endpoint, options);
            }

            if (value?.UserProvidedRetryPolicy is true)
            {
                writer.WritePropertyName("retry-policy");
                JsonSerializer.Serialize(writer, value.RetryPolicy, options);
            }

            writer.WriteEndObject();
        }
    }
}

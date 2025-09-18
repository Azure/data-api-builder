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

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
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
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                string? endpoint = null;
                AKVRetryPolicyOptions? retryPolicy = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new AzureKeyVaultOptions
                        {
                            Endpoint = endpoint,
                            RetryPolicy = retryPolicy
                        };
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
                                // Pass the replaceEnvVar setting to the retry policy converter
                                retryPolicy = JsonSerializer.Deserialize<AKVRetryPolicyOptions>(ref reader, options);
                            }

                            break;

                        default:
                            reader.Skip();
                            break;
                    }
                }
            }
            else if (reader.TokenType is JsonTokenType.Null)
            {
                return null;
            }

            throw new JsonException("Invalid AzureKeyVaultOptions format");
        }

        public override void Write(Utf8JsonWriter writer, AzureKeyVaultOptions value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}

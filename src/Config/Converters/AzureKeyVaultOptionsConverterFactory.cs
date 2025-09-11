// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Converter factory for AzureKeyVaultOptions that does not perform variable replacement.
/// This ensures we can read the raw AKV configuration needed to set up variable replacement.
/// </summary>
internal class AzureKeyVaultOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(AzureKeyVaultOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new AzureKeyVaultOptionsConverter();
    }

    private class AzureKeyVaultOptionsConverter : JsonConverter<AzureKeyVaultOptions>
    {
        /// <summary>
        /// Reads AzureKeyVaultOptions without performing variable replacement.
        /// Variable replacement will be handled in subsequent passes.
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
                                endpoint = reader.GetString();
                            }

                            break;

                        case "retry-policy":
                            if (reader.TokenType is JsonTokenType.StartObject)
                            {
                                // Use the existing AKVRetryPolicyOptionsConverter without variable replacement
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

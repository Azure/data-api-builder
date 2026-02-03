// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Custom JSON converter for EmbeddingsOptions that handles proper deserialization
/// of the configuration properties including environment variable replacement.
/// </summary>
internal class EmbeddingsOptionsConverterFactory : JsonConverterFactory
{
    private readonly DeserializationVariableReplacementSettings? _replacementSettings;

    public EmbeddingsOptionsConverterFactory(DeserializationVariableReplacementSettings? replacementSettings = null)
    {
        _replacementSettings = replacementSettings;
    }

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EmbeddingsOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EmbeddingsOptionsConverter(_replacementSettings);
    }

    private class EmbeddingsOptionsConverter : JsonConverter<EmbeddingsOptions>
    {
        private readonly DeserializationVariableReplacementSettings? _replacementSettings;

        public EmbeddingsOptionsConverter(DeserializationVariableReplacementSettings? replacementSettings)
        {
            _replacementSettings = replacementSettings;
        }

        public override EmbeddingsOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected start of object.");
            }

            bool? enabled = null;
            EmbeddingProviderType? provider = null;
            string? baseUrl = null;
            string? apiKey = null;
            string? model = null;
            string? apiVersion = null;
            int? dimensions = null;
            int? timeoutMs = null;
            EmbeddingsEndpointOptions? endpoint = null;
            EmbeddingsHealthCheckConfig? health = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected property name.");
                }

                string? propertyName = reader.GetString()?.ToLowerInvariant();
                reader.Read();

                switch (propertyName)
                {
                    case "enabled":
                        enabled = reader.GetBoolean();
                        break;
                    case "provider":
                        string? providerStr = reader.GetString();
                        if (providerStr is not null)
                        {
                            provider = providerStr.ToLowerInvariant() switch
                            {
                                "azure-openai" => EmbeddingProviderType.AzureOpenAI,
                                "openai" => EmbeddingProviderType.OpenAI,
                                _ => throw new JsonException($"Unknown provider: {providerStr}")
                            };
                        }
                        break;
                    case "base-url":
                        baseUrl = JsonSerializer.Deserialize<string>(ref reader, options);
                        break;
                    case "api-key":
                        apiKey = JsonSerializer.Deserialize<string>(ref reader, options);
                        break;
                    case "model":
                        model = JsonSerializer.Deserialize<string>(ref reader, options);
                        break;
                    case "api-version":
                        apiVersion = JsonSerializer.Deserialize<string>(ref reader, options);
                        break;
                    case "dimensions":
                        dimensions = reader.GetInt32();
                        break;
                    case "timeout-ms":
                        timeoutMs = reader.GetInt32();
                        break;
                    case "endpoint":
                        endpoint = JsonSerializer.Deserialize<EmbeddingsEndpointOptions>(ref reader, options);
                        break;
                    case "health":
                        health = JsonSerializer.Deserialize<EmbeddingsHealthCheckConfig>(ref reader, options);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (provider is null)
            {
                throw new JsonException("Missing required property: provider");
            }

            if (baseUrl is null)
            {
                throw new JsonException("Missing required property: base-url");
            }

            if (apiKey is null)
            {
                throw new JsonException("Missing required property: api-key");
            }

            return new EmbeddingsOptions(
                Provider: provider.Value,
                BaseUrl: baseUrl,
                ApiKey: apiKey,
                Enabled: enabled,
                Model: model,
                ApiVersion: apiVersion,
                Dimensions: dimensions,
                TimeoutMs: timeoutMs,
                Endpoint: endpoint,
                Health: health);
        }

        public override void Write(Utf8JsonWriter writer, EmbeddingsOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WriteBoolean("enabled", value.Enabled);

            // Write provider
            string providerStr = value.Provider switch
            {
                EmbeddingProviderType.AzureOpenAI => "azure-openai",
                EmbeddingProviderType.OpenAI => "openai",
                _ => throw new JsonException($"Unknown provider: {value.Provider}")
            };
            writer.WriteString("provider", providerStr);

            writer.WriteString("base-url", value.BaseUrl);
            writer.WriteString("api-key", value.ApiKey);

            if (value.Model is not null)
            {
                writer.WriteString("model", value.Model);
            }

            if (value.ApiVersion is not null)
            {
                writer.WriteString("api-version", value.ApiVersion);
            }

            if (value.Dimensions is not null)
            {
                writer.WriteNumber("dimensions", value.Dimensions.Value);
            }

            if (value.TimeoutMs is not null)
            {
                writer.WriteNumber("timeout-ms", value.TimeoutMs.Value);
            }

            if (value.Endpoint is not null)
            {
                writer.WritePropertyName("endpoint");
                JsonSerializer.Serialize(writer, value.Endpoint, options);
            }

            if (value.Health is not null)
            {
                writer.WritePropertyName("health");
                JsonSerializer.Serialize(writer, value.Health, options);
            }

            writer.WriteEndObject();
        }
    }
}

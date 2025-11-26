// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class AutoentityTemplateConverter : JsonConverter<AutoentityTemplate>
{
    // Settings for variable replacement during deserialization.
    private readonly DeserializationVariableReplacementSettings? _replacementSettings;

    /// <param name="replacementSettings">Settings for variable replacement during deserialization.
    /// If null, no variable replacement will be performed.</param>
    public AutoentityTemplateConverter(DeserializationVariableReplacementSettings? replacementSettings = null)
    {
        _replacementSettings = replacementSettings;
    }

    /// <inheritdoc/>
    public override AutoentityTemplate? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartObject)
        {
            // Create converters for each of the sub-properties.
            EntityRestOptionsConverterFactory restOptionsConverterFactory = new(_replacementSettings);
            JsonConverter<EntityRestOptions> restOptionsConverter = (JsonConverter<EntityRestOptions>)(restOptionsConverterFactory.CreateConverter(typeof(EntityRestOptions), options)
                ?? throw new JsonException("Unable to create converter for EntityRestOptions"));

            EntityGraphQLOptionsConverterFactory graphQLOptionsConverterFactory = new(_replacementSettings);
            JsonConverter<EntityGraphQLOptions> graphQLOptionsConverter = (JsonConverter<EntityGraphQLOptions>)(graphQLOptionsConverterFactory.CreateConverter(typeof(EntityGraphQLOptions), options)
                ?? throw new JsonException("Unable to create converter for EntityGraphQLOptions"));

            EntityHealthOptionsConvertorFactory healthOptionsConverterFactory = new();
            JsonConverter<EntityHealthCheckConfig> healthOptionsConverter = (JsonConverter<EntityHealthCheckConfig>)(healthOptionsConverterFactory.CreateConverter(typeof(EntityHealthCheckConfig), options)
                ?? throw new JsonException("Unable to create converter for EntityHealthCheckConfig"));

            EntityCacheOptionsConverterFactory cacheOptionsConverterFactory = new(_replacementSettings);
            JsonConverter<EntityCacheOptions> cacheOptionsConverter = (JsonConverter<EntityCacheOptions>)(cacheOptionsConverterFactory.CreateConverter(typeof(EntityCacheOptions), options)
                ?? throw new JsonException("Unable to create converter for EntityCacheOptions"));

            // Initialize all sub-properties to null.
            EntityRestOptions? rest = null;
            EntityGraphQLOptions? graphQL = null;
            EntityHealthCheckConfig? health = null;
            EntityCacheOptions? cache = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new AutoentityTemplate(rest, graphQL, health, cache);
                }

                string? propertyName = reader.GetString();

                reader.Read();
                switch (propertyName)
                {
                    case "rest":
                        rest = restOptionsConverter.Read(ref reader, typeof(EntityRestOptions), options);
                        break;

                    case "graphql":
                        graphQL = graphQLOptionsConverter.Read(ref reader, typeof(EntityGraphQLOptions), options);
                        break;

                    case "mcp":
                        // TODO: Add MCP support for autoentities needed.
                        break;

                    case "health":
                        health = healthOptionsConverter.Read(ref reader, typeof(EntityHealthCheckConfig), options);
                        break;

                    case "cache":
                        cache = cacheOptionsConverter.Read(ref reader, typeof(EntityCacheOptions), options);
                        break;

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }
        }

        throw new JsonException("Unable to read the Autoentities Pattern Options");
    }

    /// <summary>
    /// When writing the autoentities.template back to a JSON file, only write the properties
    /// if they are user provided. This avoids polluting the written JSON file with properties
    /// the user most likely omitted when writing the original DAB runtime config file.
    /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
    /// </summary>
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, AutoentityTemplate value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value?.UserProvidedRestOptions is true)
        {
            writer.WritePropertyName("rest");
            JsonSerializer.Serialize(writer, value.Rest, options);
        }

        if (value?.UserProvidedGraphQLOptions is true)
        {
            writer.WritePropertyName("graphql");
            JsonSerializer.Serialize(writer, value.GraphQL, options);
        }

        if (value?.UserProvidedHealthOptions is true)
        {
            writer.WritePropertyName("health");
            JsonSerializer.Serialize(writer, value.Health, options);
        }

        if (value?.UserProvidedCacheOptions is true)
        {
            writer.WritePropertyName("cache");
            JsonSerializer.Serialize(writer, value.Cache, options);
        }

        writer.WriteEndObject();
    }
}

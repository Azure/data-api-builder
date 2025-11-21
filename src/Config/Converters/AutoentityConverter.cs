// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class AutoentityConverter : JsonConverter<Autoentity>
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    public AutoentityConverter(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    /// <inheritdoc/>
    public override Autoentity? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartObject)
        {
            // Initialize all sub-properties to null.
            AutoentityPatterns? patterns = null;
            AutoentityTemplate? template = null;
            EntityPermission[]? permissions = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new Autoentity(patterns, template, permissions);
                }

                string? propertyName = reader.GetString();

                reader.Read();
                switch (propertyName)
                {
                    case "patterns":
                        AutoentityPatternsConverter patternsConverter = new(_replaceEnvVar);
                        patterns = patternsConverter.Read(ref reader, typeof(AutoentityPatterns), options);
                        break;

                    case "template":
                        AutoentityTemplateConverter templateConverter = new(_replaceEnvVar);
                        template = templateConverter.Read(ref reader, typeof(AutoentityTemplate), options);
                        break;

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }
        }

        throw new JsonException("Unable to read the Autoentities");
    }

    /// <summary>
    /// When writing the autoentities back to a JSON file, only write the properties
    /// if they are user provided. This avoids polluting the written JSON file with properties
    /// the user most likely omitted when writing the original DAB runtime config file.
    /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
    /// </summary>
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Autoentity value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        AutoentityPatterns? patterns = value?.Patterns;
        if (patterns?.UserProvidedIncludeOptions is true
            || patterns?.UserProvidedExcludeOptions is true
            || patterns?.UserProvidedNameOptions is true)
        {
            AutoentityPatternsConverter autoentityPatternsConverter = options.GetConverter(typeof(AutoentityPatterns)) as AutoentityPatternsConverter ??
                throw new JsonException("Failed to get autoentities.patterns options converter");
            writer.WritePropertyName("patterns");
            autoentityPatternsConverter.Write(writer, patterns, options);
        }

        AutoentityTemplate? template = value?.Template;
        if (template?.UserProvidedRestOptions is true
            || template?.UserProvidedGraphQLOptions is true
            || template?.UserProvidedHealthOptions is true
            || template?.UserProvidedCacheOptions is true)
        {
            AutoentityTemplateConverter autoentityTemplateConverter = options.GetConverter(typeof(AutoentityTemplate)) as AutoentityTemplateConverter ??
                throw new JsonException("Failed to get autoentities.template options converter");
            writer.WritePropertyName("template");
            autoentityTemplateConverter.Write(writer, template, options);
        }

        writer.WriteEndObject();
    }
}

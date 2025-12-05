// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class AutoentityPatternsConverter : JsonConverter<AutoentityPatterns>
{
    // Settings for variable replacement during deserialization.
    private readonly DeserializationVariableReplacementSettings? _replacementSettings;

    /// <param name="replacementSettings">Settings for variable replacement during deserialization.
    /// If null, no variable replacement will be performed.</param>
    public AutoentityPatternsConverter(DeserializationVariableReplacementSettings? replacementSettings = null)
    {
        _replacementSettings = replacementSettings;
    }

    /// <inheritdoc/>
    public override AutoentityPatterns? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartObject)
        {
            string[]? include = null;
            string[]? exclude = null;
            string? name = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new AutoentityPatterns(include, exclude, name);
                }

                string? propertyName = reader.GetString();

                reader.Read();
                switch (propertyName)
                {
                    case "include":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            List<string> includeList = new();

                            if (reader.TokenType == JsonTokenType.StartArray)
                            {
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    string? value = reader.DeserializeString(_replacementSettings);
                                    if (value is not null)
                                    {
                                        includeList.Add(value);
                                    }
                                }

                                include = includeList.ToArray();
                            }
                            else
                            {
                                throw new JsonException("Expected array for 'include' property.");
                            }
                        }

                        break;

                    case "exclude":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            List<string> excludeList = new();

                            if (reader.TokenType == JsonTokenType.StartArray)
                            {
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    string? value = reader.DeserializeString(_replacementSettings);
                                    if (value is not null)
                                    {
                                        excludeList.Add(value);
                                    }
                                }

                                exclude = excludeList.ToArray();
                            }
                            else
                            {
                                throw new JsonException("Expected array for 'exclude' property.");
                            }
                        }

                        break;

                    case "name":
                        name = reader.DeserializeString(_replacementSettings);
                        break;

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }
        }

        throw new JsonException("Failed to read the Autoentities Pattern Options");
    }

    /// <summary>
    /// When writing the autoentities.patterns back to a JSON file, only write the properties
    /// if they are user provided. This avoids polluting the written JSON file with properties
    /// the user most likely omitted when writing the original DAB runtime config file.
    /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
    /// </summary>
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, AutoentityPatterns value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value?.UserProvidedIncludeOptions is true)
        {
            writer.WritePropertyName("include");
            writer.WriteStartArray();
            foreach (string? include in value.Include)
            {
                JsonSerializer.Serialize(writer, include, options);
            }

            writer.WriteEndArray();
        }

        if (value?.UserProvidedExcludeOptions is true)
        {
            writer.WritePropertyName("exclude");
            writer.WriteStartArray();
            foreach (string? exclude in value.Exclude)
            {
                JsonSerializer.Serialize(writer, exclude, options);
            }

            writer.WriteEndArray();
        }

        if (value?.UserProvidedNameOptions is true)
        {
            writer.WritePropertyName("name");
            JsonSerializer.Serialize(writer, value.Name, options);
        }

        writer.WriteEndObject();
    }
}

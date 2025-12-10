// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntityRestOptionsConverterFactory : JsonConverterFactory
{
    /// Settings for variable replacement during deserialization.
    private readonly DeserializationVariableReplacementSettings? _replacementSettings;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityRestOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityRestOptionsConverter(_replacementSettings);
    }

    /// <param name="replacementSettings">Settings for variable replacement during deserialization.
    /// If null, no variable replacement will be performed.</param>
    internal EntityRestOptionsConverterFactory(DeserializationVariableReplacementSettings? replacementSettings = null)
    {
        _replacementSettings = replacementSettings;
    }

    internal class EntityRestOptionsConverter : JsonConverter<EntityRestOptions>
    {
        // Settings for variable replacement during deserialization.
        private readonly DeserializationVariableReplacementSettings? _replacementSettings;

        /// <param name="replacementSettings">Settings for variable replacement during deserialization.
        /// If null, no variable replacement will be performed.</param>
        public EntityRestOptionsConverter(DeserializationVariableReplacementSettings? replacementSettings)
        {
            _replacementSettings = replacementSettings;
        }

        /// <inheritdoc/>
        public override EntityRestOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                EntityRestOptions restOptions = new(Methods: Array.Empty<SupportedHttpVerb>(), Path: null, Enabled: true);
                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string? propertyName = reader.GetString();

                    switch (propertyName)
                    {
                        case "path":
                            reader.Read();

                            if (reader.TokenType is JsonTokenType.String || reader.TokenType is JsonTokenType.Null)
                            {
                                restOptions = restOptions with { Path = reader.DeserializeString(_replacementSettings) };
                                break;
                            }

                            throw new JsonException($"The value of {propertyName} must be a string. Found {reader.TokenType}.");

                        case "methods":
                            List<SupportedHttpVerb> methods = new();
                            while (reader.Read())
                            {
                                if (reader.TokenType is JsonTokenType.StartArray)
                                {
                                    continue;
                                }

                                if (reader.TokenType is JsonTokenType.EndArray)
                                {
                                    break;
                                }

                                methods.Add(EnumExtensions.Deserialize<SupportedHttpVerb>(reader.DeserializeString(new DeserializationVariableReplacementSettings())!));
                            }

                            restOptions = restOptions with { Methods = methods.ToArray() };
                            break;

                        case "enabled":
                            reader.Read();
                            restOptions = restOptions with { Enabled = reader.GetBoolean() };
                            break;

                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }

                return restOptions;
            }

            if (reader.TokenType is JsonTokenType.String)
            {
                return new EntityRestOptions(Array.Empty<SupportedHttpVerb>(), reader.DeserializeString(_replacementSettings), true);
            }

            if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
            {
                bool enabled = reader.GetBoolean();
                return new EntityRestOptions(
                    Methods: Array.Empty<SupportedHttpVerb>(),
                    Path: null,
                    Enabled: enabled);
            }

            throw new JsonException();
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, EntityRestOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value.Enabled);

            if (value.Path is not null)
            {
                writer.WriteString("path", value.Path);
            }
            else if (value.Path is null && options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull)
            {
                writer.WriteNull("path");
            }

            if (value.Methods is not null && value.Methods.Length > 0)
            {

                writer.WriteStartArray("methods");
                foreach (SupportedHttpVerb method in value.Methods)
                {
                    writer.WriteStringValue(JsonSerializer.SerializeToElement(method, options).GetString());
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }
}

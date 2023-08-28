// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntityGraphQLOptionsConverterFactory : JsonConverterFactory
{
    /// Determines whether to replace environment variable with its
    /// value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityGraphQLOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityGraphQLOptionsConverter(_replaceEnvVar);
    }

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    internal EntityGraphQLOptionsConverterFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class EntityGraphQLOptionsConverter : JsonConverter<EntityGraphQLOptions>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        public EntityGraphQLOptionsConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        /// <inheritdoc/>
        public override EntityGraphQLOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                string singular = string.Empty;
                string plural = string.Empty;
                bool enabled = true;
                GraphQLOperation? operation = null;

                while (reader.Read())
                {
                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        return new EntityGraphQLOptions(singular, plural, enabled, operation);
                    }

                    string? property = reader.GetString();
                    reader.Read();

                    switch (property)
                    {
                        case "enabled":
                            enabled = reader.GetBoolean();
                            break;
                        case "type":
                            if (reader.TokenType is JsonTokenType.String)
                            {
                                singular = reader.DeserializeString(_replaceEnvVar) ?? string.Empty;
                            }
                            else if (reader.TokenType is JsonTokenType.StartObject)
                            {
                                while (reader.Read())
                                {
                                    if (reader.TokenType is JsonTokenType.EndObject)
                                    {
                                        break;
                                    }

                                    if (reader.TokenType is JsonTokenType.PropertyName)
                                    {
                                        string? property2 = reader.GetString();
                                        reader.Read();
                                        // it's possible that we won't end up setting the value for singular
                                        // or plural, but this will then be determined from the entity name
                                        // when the RuntimeEntities constructor is invoked later in the
                                        // deserialization process.
                                        switch (property2)
                                        {
                                            case "singular":
                                                singular = reader.DeserializeString(_replaceEnvVar) ?? string.Empty;
                                                break;
                                            case "plural":
                                                plural = reader.DeserializeString(_replaceEnvVar) ?? string.Empty;
                                                break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                throw new JsonException($"The value for the 'type' property must be a string or an object, but was {reader.TokenType}");
                            }

                            break;

                        case "operation":
                            string? op = reader.DeserializeString(_replaceEnvVar);

                            if (op is not null)
                            {
                                operation = Enum.Parse<GraphQLOperation>(op, ignoreCase: true);
                            }

                            break;
                    }
                }
            }

            if (reader.TokenType is JsonTokenType.True)
            {
                return new EntityGraphQLOptions(Singular: string.Empty, Plural: string.Empty, Enabled: true);
            }

            if (reader.TokenType is JsonTokenType.False || reader.TokenType is JsonTokenType.Null)
            {
                return new EntityGraphQLOptions(Singular: string.Empty, Plural: string.Empty, Enabled: false);
            }

            if (reader.TokenType is JsonTokenType.String)
            {
                string? singular = reader.DeserializeString(_replaceEnvVar);
                return new EntityGraphQLOptions(singular ?? string.Empty, string.Empty);
            }

            throw new JsonException();
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, EntityGraphQLOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value.Enabled);

            if (value.Operation is not null)
            {
                writer.WritePropertyName("operation");
                JsonSerializer.Serialize(writer, value.Operation, options);
            }
            else if (value.Operation is null && options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull)
            {
                writer.WriteNull("operation");
            }

            writer.WriteStartObject("type");
            writer.WriteString("singular", value.Singular);
            writer.WriteString("plural", value.Plural);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
    }
}

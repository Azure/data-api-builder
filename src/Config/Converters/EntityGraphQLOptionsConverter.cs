// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntityGraphQLOptionsConverter : JsonConverter<EntityGraphQLOptions>
{
    /// <inheritdoc/>
    public override EntityGraphQLOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            string singular = string.Empty;
            string plural = string.Empty;
            bool enabled = true;
            GraphQLOperation? operation = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
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
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            singular = reader.DeserializeString() ?? string.Empty;
                        }
                        else if (reader.TokenType == JsonTokenType.StartObject)
                        {
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonTokenType.EndObject)
                                {
                                    break;
                                }

                                if (reader.TokenType == JsonTokenType.PropertyName)
                                {
                                    string? property2 = reader.GetString();
                                    reader.Read();
                                    switch (property2)
                                    {
                                        case "singular":
                                            singular = reader.DeserializeString() ?? string.Empty;
                                            break;
                                        case "plural":
                                            plural = reader.DeserializeString() ?? string.Empty;
                                            break;
                                    }
                                }
                            }
                        }

                        break;

                    case "operation":
                        string? op = reader.DeserializeString();

                        if (op is not null)
                        {
                            operation = Enum.Parse<GraphQLOperation>(op, ignoreCase: true);
                        }

                        break;
                }
            }
        }

        if (reader.TokenType == JsonTokenType.True)
        {
            return new EntityGraphQLOptions(Singular: string.Empty, Plural: string.Empty, Enabled: true);
        }

        if (reader.TokenType == JsonTokenType.False || reader.TokenType == JsonTokenType.Null)
        {
            return new EntityGraphQLOptions(Singular: string.Empty, Plural: string.Empty, Enabled: false);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? singular = reader.DeserializeString();
            return new EntityGraphQLOptions(singular ?? string.Empty, string.Empty);
        }

        throw new JsonException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EntityGraphQLOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("enabled", value.Enabled);

        if (value.Operation is null)
        {
            writer.WriteNull("operation");
        }
        else
        {
            writer.WritePropertyName("operation");
            JsonSerializer.Serialize(writer, value.Operation, options);
        }

        writer.WriteStartObject("type");
        writer.WriteString("singular", value.Singular);
        writer.WriteString("plural", value.Plural);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }
}

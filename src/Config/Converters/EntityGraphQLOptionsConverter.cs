// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

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
            GraphQLOperation operation = GraphQLOperation.Query;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new EntityGraphQLOptions(singular, plural, enabled, operation);
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
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
                                singular = reader.GetString() ?? string.Empty;
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
                                                singular = reader.GetString() ?? string.Empty;
                                                break;
                                            case "plural":
                                                plural = reader.GetString() ?? string.Empty;
                                                break;
                                        }
                                    }
                                }
                            }

                            break;

                        case "operation":
                            string? op = reader.GetString();
                            operation = Enum.Parse<GraphQLOperation>(op!, ignoreCase: true);
                            break;
                    }
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

        throw new JsonException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EntityGraphQLOptions value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}

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
            string? singular = null;
            string? plural = null;
            bool enabled = true;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new EntityGraphQLOptions(singular, plural, enabled);
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
                                singular = reader.GetString();
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
                                                singular = reader.GetString();
                                                break;
                                            case "plural":
                                                plural = reader.GetString();
                                                break;
                                        }
                                    }
                                }
                            }

                            break;
                    }
                }
            }
        }

        if (reader.TokenType == JsonTokenType.True)
        {
            return new EntityGraphQLOptions();
        }

        if (reader.TokenType == JsonTokenType.False || reader.TokenType == JsonTokenType.Null)
        {
            return new EntityGraphQLOptions(Enabled: false);
        }

        throw new JsonException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EntityGraphQLOptions value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}

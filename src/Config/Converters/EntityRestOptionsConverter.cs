// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntityRestOptionsConverter : JsonConverter<EntityRestOptions>
{
    /// <inheritdoc/>
    public override EntityRestOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            EntityRestOptions restOptions = new(Methods: Array.Empty<SupportedHttpVerb>(), Path: null, Enabled: true);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                string? propertyName = reader.DeserializeString();

                switch (propertyName)
                {
                    case "path":
                    {
                        reader.Read();

                        if (reader.TokenType == JsonTokenType.String)
                        {
                            restOptions = restOptions with { Path = reader.DeserializeString() };
                            break;
                        }

                        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                        {
                            restOptions = restOptions with { Enabled = reader.GetBoolean() };
                            break;
                        }

                        break;
                    }

                    case "methods":
                    {
                        List<SupportedHttpVerb> methods = new();
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.StartArray)
                            {
                                continue;
                            }

                            if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }

                            methods.Add(Enum.Parse<SupportedHttpVerb>(reader.DeserializeString()!, true));
                        }

                        restOptions = restOptions with { Methods = methods.ToArray() };
                        break;
                    }

                    case "enabled":
                    {
                        reader.Read();
                        restOptions = restOptions with { Enabled = reader.GetBoolean() };
                        break;
                    }

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }

            return restOptions;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return new EntityRestOptions(EntityRestOptions.DEFAULT_SUPPORTED_VERBS, reader.DeserializeString(), true);
        }

        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
        {
            return new EntityRestOptions(EntityRestOptions.DEFAULT_SUPPORTED_VERBS, null, reader.GetBoolean());
        }

        throw new JsonException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EntityRestOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("enabled", value.Enabled);
        writer.WriteString("path", value.Path);
        writer.WriteStartArray("methods");
        foreach (SupportedHttpVerb method in value.Methods)
        {
            writer.WriteStringValue(JsonSerializer.SerializeToElement(method, options).GetString());
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

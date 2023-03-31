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
            EntityRestOptions restOptions = new(Path: null, Methods: Array.Empty<string>(), Enabled: true);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                string? propertyName = reader.GetString();

                switch (propertyName)
                {
                    case "path":
                    {
                        reader.Read();

                        if (reader.TokenType == JsonTokenType.String)
                        {
                            restOptions = restOptions with { Path = reader.GetString() };
                            break;
                        }

                        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                        {
                            restOptions = restOptions with { Enabled = reader.GetBoolean() };
                            break;
                        }

                        Console.WriteLine($"Unable to handle $.rest.path with token {reader.TokenType}");
                        break;
                    }

                    case "methods":
                        List<string> methods = new();
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

                            methods.Add(reader.GetString()!);
                        }

                        restOptions = restOptions with { Methods = methods.ToArray() };
                        break;
                }
            }

            return restOptions;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return new EntityRestOptions(reader.GetString(), Array.Empty<string>(), true);
        }

        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
        {
            return new EntityRestOptions(null, Array.Empty<string>(), reader.GetBoolean());
        }

        throw new JsonException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EntityRestOptions value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}

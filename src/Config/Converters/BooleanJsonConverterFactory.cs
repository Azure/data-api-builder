// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Custom string json converter factory to replace environment variables and other variable patterns
/// during deserialization using the DeserializationVariableReplacementSettings.
/// </summary>
public class BoolJsonConverter : JsonConverter<bool>
{
    
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {

            throw new JsonException();
        }

        if (reader.TokenType == JsonTokenType.String)
        {

            string? tempBoolean = JsonSerializer.Deserialize<string>(ref reader, options);

            bool result = tempBoolean?.ToLower() switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => throw new JsonException($"Invalid enabled value: {tempBoolean}. Specify either true or false."),
            };

            return result;
        }
        else
        {
            return reader.GetBoolean();
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// JSON converter for boolean values that also supports string representations such as
/// "true", "false", "1", and "0". Any environment variable replacement is handled by
/// other converters (for example, the string converter) before the value is parsed here.
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
                //numeric values have to be checked here as they may come from string replacement 
                "true" or "1" => true,
                "false" or "0" => false,
                _ => throw new JsonException($"Invalid boolean value: {tempBoolean}. Specify either true or false."),
            };

            return result;
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            bool result = reader.GetInt32() switch
            {
                1 => true,
                0 => false,
                _ => throw new JsonException($"Invalid boolean value. Specify either true or false."),
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

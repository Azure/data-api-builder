// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Config.Converters;

static internal class Utf8JsonReaderExtensions
{
    /// <summary>
    /// Reads a string from the <c>Utf8JsonReader</c> by using the deserialize method rather than GetString.
    /// This will ensure that the <see cref="StringJsonConverterFactory"/> is used and environment variable
    /// substitution is applied.
    /// </summary>
    /// <param name="reader">The reader that we want to pull the string from.</param>
    /// <returns>The result of deserialization.</returns>
    /// <exception cref="JsonException">Thrown if the <see cref="JsonTokenType"/> is not String.</exception>
    public static string? DeserializeString(this Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"Expected string token type, received: {reader.TokenType}");
        }

        JsonSerializerOptions options = new();
        options.Converters.Add(new StringJsonConverterFactory());
        return JsonSerializer.Deserialize<string>(ref reader, options);
    }
}

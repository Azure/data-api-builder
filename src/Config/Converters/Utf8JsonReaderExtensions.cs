// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace Azure.DataApiBuilder.Config.Converters;

static public class Utf8JsonReaderExtensions
{
    /// <summary>
    /// Reads a string from the <c>Utf8JsonReader</c> by using the deserialize method rather than GetString.
    /// This will ensure that the <see cref="StringJsonConverterFactory"/> is used and environment variable
    /// substitution is applied.
    /// </summary>
    /// <param name="reader">The reader that we want to pull the string from.</param>
    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    /// <param name="replacementFailureMode">The failure mode to use when replacing environment variables.</param>
    /// <returns>The result of deserialization.</returns>
    /// <exception cref="JsonException">Thrown if the <see cref="JsonTokenType"/> is not String.</exception>
    public static string? DeserializeString(this Utf8JsonReader reader,
        bool replaceEnvVar,
        EnvironmentVariableReplacementFailureMode replacementFailureMode = EnvironmentVariableReplacementFailureMode.Throw)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"Expected string token type, received: {reader.TokenType}");
        }

        // Add the StringConverterFactory so that we can do environment variable substitution.
        JsonSerializerOptions options = new();
        if (replaceEnvVar)
        {
            options.Converters.Add(new StringJsonConverterFactory(replacementFailureMode));
        }

        return JsonSerializer.Deserialize<string>(ref reader, options);
    }

    /// <summary>
    /// Replaces placeholders for environment variables in the given JSON string and returns the new JSON string.
    /// </summary>
    /// <param name="inputJson">The JSON string to process.</param>
    /// <exception cref="JsonException">Thrown when the input is null or when an error occurs while processing the JSON.</exception>
    public static string? ReplaceEnvVarsInJson(string? inputJson)
    {
        try
        {
            if (inputJson is null)
            {
                throw new JsonException("Input JSON string is null.");
            }

            // Load the JSON string
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            using MemoryStream stream = new();
            using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });

            // Replace environment variable placeholders
            ReplaceEnvVarsAndWrite(doc.RootElement, writer);
            writer.Flush();

            // return the final JSON as a string
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (Exception e)
        {
            throw new JsonException("Failed to replace environment variables in given JSON. " + e.Message);
        }
    }

    /// <summary>
    /// Replaces placeholders for environment variables in a JSON element and writes the result to a JSON writer.
    /// </summary>
    /// <param name="element">The JSON element to process.</param>
    /// <param name="writer">The JSON writer to write the result to.</param>
    private static void ReplaceEnvVarsAndWrite(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    // Recursively process each property of the object
                    ReplaceEnvVarsAndWrite(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    // Recursively process each item of the array
                    ReplaceEnvVarsAndWrite(item, writer);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                string? value = element.GetString();
                if (value is not null && value.StartsWith("@env('") && value.EndsWith("')"))
                {
                    string envVar = value[6..^2];

                    // Check if the environment variable is set
                    if (!Environment.GetEnvironmentVariables().Contains(envVar))
                    {
                        throw new ArgumentException($"Environment variable '{envVar}' is not set.");
                    }

                    string? envValue = Environment.GetEnvironmentVariable(envVar);

                    // Write the value of the environment variable to the JSON writer
                    if (bool.TryParse(envValue, out bool boolValue))
                    {
                        writer.WriteBooleanValue(boolValue);
                    }
                    else if (int.TryParse(envValue, out int intValue))
                    {
                        writer.WriteNumberValue(intValue);
                    }
                    else if (double.TryParse(envValue, out double doubleValue))
                    {
                        writer.WriteNumberValue(doubleValue);
                    }
                    else
                    {
                        writer.WriteStringValue(envValue);
                    }
                }
                else
                {
                    writer.WriteStringValue(value);
                }

                break;
            default:
                // Write the JSON element to the writer as is
                element.WriteTo(writer);
                break;
        }
    }
}

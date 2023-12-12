// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntitySourceConverterFactory : JsonConverterFactory
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntitySource));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntitySourceConverter(_replaceEnvVar);
    }

    /// <param name="replaceEnvVar">Whether to replace environment variable with its
    /// value or not while deserializing.</param>
    internal EntitySourceConverterFactory(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    private class EntitySourceConverter : JsonConverter<EntitySource>
    {
        // Determines whether to replace environment variable with its
        // value or not while deserializing.
        private bool _replaceEnvVar;

        /// <param name="replaceEnvVar">Whether to replace environment variable with its
        /// value or not while deserializing.</param>
        public EntitySourceConverter(bool replaceEnvVar)
        {
            _replaceEnvVar = replaceEnvVar;
        }

        public override EntitySource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? obj = reader.DeserializeString(_replaceEnvVar);
                return new EntitySource(obj ?? string.Empty, EntitySourceType.Table, new(), Array.Empty<string>());
            }

            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntitySourceConverterFactory));

            EntitySource? source = JsonSerializer.Deserialize<EntitySource>(ref reader, innerOptions);

            if (source?.Parameters is not null)
            {
                // If we get parameters back the value field will be JsonElement, since that's what System.Text.Json uses for the `object` type.
                // But we want to convert that to a CLR type so we can use it in our code and avoid having to do our own type checking
                // and casting elsewhere.
                return source with { Parameters = source.Parameters.ToDictionary(p => p.Key, p => GetClrValue((JsonElement)p.Value)) };
            }

            return source;
        }

        private static object GetClrValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => GetNumberValue(element),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => element.ToString()
            };
        }

        /// <summary>
        /// Attempts to get the correct numeric value from the <see cref="JsonElement"/>.
        /// If all possible numeric values are exhausted, the raw text is returned.
        /// </summary>
        /// <param name="element">JSON element to extract the value from.</param>
        /// <returns>The parsed value as a CLR type.</returns>
        private static object GetNumberValue(JsonElement element)
        {
            if (element.TryGetInt32(out int intValue))
            {
                return intValue;
            }

            if (element.TryGetDecimal(out decimal decimalValue))
            {
                return decimalValue;
            }

            if (element.TryGetDouble(out double doubleValue))
            {
                return doubleValue;
            }

            if (element.TryGetInt64(out long longValue))
            {
                return longValue;
            }

            if (element.TryGetUInt32(out uint uintValue))
            {
                return uintValue;
            }

            if (element.TryGetUInt64(out ulong ulongValue))
            {
                return ulongValue;
            }

            if (element.TryGetSingle(out float floatValue))
            {
                return floatValue;
            }

            return element.GetRawText();
        }

        public override void Write(Utf8JsonWriter writer, EntitySource value, JsonSerializerOptions options)
        {
            // Remove the converter so we don't recurse.
            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntitySourceConverterFactory));

            JsonSerializer.Serialize(writer, value, innerOptions);
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

public static class EnumExtensions
{
    /// <summary>
    /// Used to convert a string to an enum value.
    /// This will be used when we found a string value, such as CLI input, and need to convert it to an enum value.
    /// </summary>
    /// <typeparam name="T">The enum to deserialize as.</typeparam>
    /// <param name="value">The string value.</param>
    /// <returns>The deserialized enum value.</returns>
    public static T Deserialize<T>(string value) where T : struct, Enum
    {
        EnumMemberJsonEnumConverterFactory.JsonStringEnumConverterEx<T> converter = new();

        ReadOnlySpan<byte> bytes = new(Encoding.UTF8.GetBytes($"\"{value}\""));

        Utf8JsonReader reader = new(bytes);
        // We need to read the first token to get the reader into a state where it can read the value as a string.
        _ = reader.Read();
        return converter.Read(ref reader, typeof(T), new JsonSerializerOptions());
    }

    /// <summary>
    /// Used to convert an enum value to a string in a way that we gracefully handle failures.
    /// </summary>
    /// <typeparam name="T">The enum to deserialize as.</typeparam>
    /// <param name="value">The string value.</param>
    /// <param name="enum">The deserialized enum value.</param>
    /// <returns><c>True</c> if successful, <c>False</c> if not.</returns>
    public static bool TryDeserialize<T>(string value, [NotNullWhen(true)] out T? @enum) where T : struct, Enum
    {
        try
        {
            @enum = Deserialize<T>(value);
            return true;
        }
        catch
        {
            // We're not doing anything specific with the exception, so we can just ignore it.
        }

        @enum = null;
        return false;
    }

    public static string GenerateMessageForInvalidInput<T>(string invalidType)
        where T : struct, Enum
        => $"Invalid Source Type: {invalidType}. Valid values are: {string.Join(",", Enum.GetNames<T>())}";
}

/// <summary>
/// This converter is used to convert Enums to and from strings in a way that uses the
/// <see cref="EnumMemberAttribute"/> serialization attribute.
/// </summary>
internal class EnumMemberJsonEnumConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum;
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return (JsonConverter?)Activator.CreateInstance(
            typeof(JsonStringEnumConverterEx<>).MakeGenericType(typeToConvert)
            );
    }

    internal class JsonStringEnumConverterEx<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        private readonly Dictionary<TEnum, string> _enumToString = new();
        private readonly Dictionary<string, TEnum> _stringToEnum = new();

        public JsonStringEnumConverterEx()
        {
            Type type = typeof(TEnum);
            TEnum[] values = Enum.GetValues<TEnum>();

            foreach (TEnum value in values)
            {
                MemberInfo enumMember = type.GetMember(value.ToString())[0];
                EnumMemberAttribute? attr = enumMember.GetCustomAttributes(typeof(EnumMemberAttribute), false)
                  .Cast<EnumMemberAttribute>()
                  .FirstOrDefault();

                _stringToEnum.Add(value.ToString().ToLower(), value);

                if (attr?.Value is not null)
                {
                    _enumToString.Add(value, attr.Value);
                    _stringToEnum.Add(attr.Value, value);
                }
                else
                {
                    _enumToString.Add(value, value.ToString().ToLower());
                }
            }
        }

        /// <inheritdoc/>
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Always replace env variable in case of Enum otherwise string to enum conversion will fail.
            string? stringValue = reader.DeserializeString(new());

            if (stringValue == null)
            {
                throw new JsonException($"null is not a valid enum value of {typeof(TEnum)}");
            }

            if (_stringToEnum.TryGetValue(stringValue.ToLower(), out TEnum enumValue))
            {
                return enumValue;
            }

            throw new JsonException($"The value {stringValue} is not a valid enum value of {typeof(TEnum)}");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(_enumToString[value]);
        }
    }
}

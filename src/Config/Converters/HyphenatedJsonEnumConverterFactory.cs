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
    public static T Deserialize<T>(string value) where T : struct, Enum
    {
        HyphenatedJsonEnumConverterFactory.JsonStringEnumConverterEx<T> converter = new();

        ReadOnlySpan<byte> bytes = new(Encoding.UTF8.GetBytes($"\"{value}\""));

        Utf8JsonReader reader = new(bytes);
        // We need to read the first token to get the reader into a state where it can read the value as a string.
        reader.Read();
        return converter.Read(ref reader, typeof(T), new JsonSerializerOptions());
    }

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

internal class HyphenatedJsonEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum;
    }

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

                if (attr?.Value != null)
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

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? stringValue = reader.GetString();

            if (_stringToEnum.TryGetValue(stringValue!.ToLower(), out TEnum enumValue))
            {
                return enumValue;
            }

            throw new JsonException($"The value {stringValue} is not a valid enum value.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(_enumToString[value]);
        }
    }
}

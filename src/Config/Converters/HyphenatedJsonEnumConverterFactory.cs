// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.Converters;

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

    private class JsonStringEnumConverterEx<TEnum> : JsonConverter<TEnum> where TEnum : struct, System.Enum
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

            if (_stringToEnum.TryGetValue(stringValue!, out TEnum enumValue))
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

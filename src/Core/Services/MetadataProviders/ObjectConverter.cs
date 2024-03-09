// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    public class ObjectConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out int intValue))
                    {
                        return intValue;
                    }
                    else if (reader.TryGetInt64(out long longValue))
                    {
                        return longValue;
                    }
                    else if (reader.TryGetDecimal(out decimal decimalValue))
                    {
                        return decimalValue;
                    }
                    else if (reader.TryGetSingle(out float floatValue))
                    {
                        return floatValue;
                    }
                    else if (reader.TryGetDouble(out double doubleValue))
                    {
                        return doubleValue;
                    }
                    else
                    {
                        throw new JsonException($"Unable to deserialize number value. Token: {reader.TokenType}");
                    }

                case JsonTokenType.String:
                    string stringValue = reader.GetString()!;
                    if (Guid.TryParse(stringValue, out Guid guidValue))
                    {
                        return guidValue;
                    }
                    else if (DateTime.TryParse(stringValue, out DateTime dateTimeValue))
                    {
                        return dateTimeValue;
                    }
                    else if (DateTimeOffset.TryParse(stringValue, out DateTimeOffset dateTimeOffsetValue))
                    {
                        return dateTimeOffsetValue;
                    }
                    else if (TimeOnly.TryParse(stringValue, out TimeOnly timeOnlyValue))
                    {
                        return timeOnlyValue;
                    }
                    else
                    {
                        return stringValue;
                    }

                case JsonTokenType.True:
                    return true;

                case JsonTokenType.False:
                    return false;

                case JsonTokenType.Null:
                    return null!;

                case JsonTokenType.StartArray:
                    return JsonSerializer.Deserialize<object[]>(ref reader, options)!;

                case JsonTokenType.StartObject:
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader, options)!;

                default:
                    throw new JsonException($"Unable to deserialize object. Unexpected token type: {reader.TokenType}");
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case int intValue:
                    writer.WriteNumberValue(intValue);
                    break;
                case long longValue:
                    writer.WriteNumberValue(longValue);
                    break;
                case decimal decimalValue:
                    writer.WriteNumberValue(decimalValue);
                    break;
                case float floatValue:
                    writer.WriteNumberValue(floatValue);
                    break;
                case double doubleValue:
                    writer.WriteNumberValue(doubleValue);
                    break;
                case Guid guidValue:
                    writer.WriteStringValue(guidValue);
                    break;
                case string stringValue:
                    writer.WriteStringValue(stringValue);
                    break;
                case bool boolValue:
                    writer.WriteBooleanValue(boolValue);
                    break;
                case byte[] byteArrayValue:
                    writer.WriteBase64StringValue(byteArrayValue);
                    break;
                case DateTimeOffset dateTimeOffsetValue:
                    writer.WriteStringValue(dateTimeOffsetValue);
                    break;
                case DateTime dateTimeValue:
                    writer.WriteStringValue(dateTimeValue);
                    break;
                case TimeOnly timeOnlyValue:
                    writer.WriteStringValue(timeOnlyValue.ToString());
                    break;
                default:
                    throw new JsonException($"Unable to serialize object. Unexpected type: {value.GetType()}");
            }
        }
    }
}

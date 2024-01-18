// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters
{
    internal class NestedMutationOptionsConverter : JsonConverter<NestedMutationOptions>
    {

        private readonly NestedInsertOptionsConverter _nestedInsertOptionsConverter;

        public NestedMutationOptionsConverter(JsonSerializerOptions options)
        {
            _nestedInsertOptionsConverter = options.GetConverter(typeof(NestedInsertOptions)) as NestedInsertOptionsConverter ??
                                            throw new JsonException("Failed to get nested insert options converter");
        }

        public override NestedMutationOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new NestedMutationOptions(new(enabled: false));
            }

            if(reader.TokenType is JsonTokenType.StartObject)
            {
                NestedMutationOptions? nestedMutationOptions = new(new(enabled: false));

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string? propertyName = reader.GetString();
                    switch (propertyName)
                    {
                        case "inserts":
                            reader.Read();
                            nestedMutationOptions = new(_nestedInsertOptionsConverter.Read(ref reader, typeToConvert, options));
                            break;

                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }

                return nestedMutationOptions;
            }
            
            throw new JsonException();
        }
        public override void Write(Utf8JsonWriter writer, NestedMutationOptions value, JsonSerializerOptions options)
        {
            writer.WritePropertyName("nested-mutations");

            writer.WriteStartObject();

            if(value.NestedInsertOptions is not null)
            {
                _nestedInsertOptionsConverter.Write(writer, value.NestedInsertOptions, options);
            }

            writer.WriteEndObject();
        }
    }
}

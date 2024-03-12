// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters
{
    /// <summary>
    /// Converter for the nested mutation options.
    /// </summary>
    internal class NestedMutationOptionsConverter : JsonConverter<NestedMutationOptions>
    {

        private readonly NestedCreateOptionsConverter _nestedCreateOptionsConverter;

        public NestedMutationOptionsConverter(JsonSerializerOptions options)
        {
            _nestedCreateOptionsConverter = options.GetConverter(typeof(NestedCreateOptions)) as NestedCreateOptionsConverter ??
                                            throw new JsonException("Failed to get nested create options converter");
        }

        /// <inheritdoc/>
        public override NestedMutationOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                NestedMutationOptions? nestedMutationOptions = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string? propertyName = reader.GetString();
                    switch (propertyName)
                    {
                        case "create":
                            reader.Read();
                            NestedCreateOptions? nestedCreateOptions = _nestedCreateOptionsConverter.Read(ref reader, typeToConvert, options);
                            if (nestedCreateOptions is not null)
                            {
                                nestedMutationOptions = new(nestedCreateOptions);
                            }

                            break;

                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }

                return nestedMutationOptions;
            }

            throw new JsonException("Failed to read the GraphQL Nested Mutation options");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, NestedMutationOptions? value, JsonSerializerOptions options)
        {
            // If the nested mutation options is null, it is not written to the config file.
            if (value is null)
            {
                return;
            }

            writer.WritePropertyName("nested-mutations");

            writer.WriteStartObject();

            if (value.NestedCreateOptions is not null)
            {
                _nestedCreateOptionsConverter.Write(writer, value.NestedCreateOptions, options);
            }

            writer.WriteEndObject();
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters
{
    /// <summary>
    /// Converter for the multiple mutation options.
    /// </summary>
    internal class MultipleMutationOptionsConverter : JsonConverter<MultipleMutationOptions>
    {

        private readonly MultipleCreateOptionsConverter _multipleCreateOptionsConverter;

        public MultipleMutationOptionsConverter(JsonSerializerOptions options)
        {
            _multipleCreateOptionsConverter = options.GetConverter(typeof(MultipleCreateOptions)) as MultipleCreateOptionsConverter ??
                                            throw new JsonException("Failed to get multiple create options converter");
        }

        /// <inheritdoc/>
        public override MultipleMutationOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                MultipleMutationOptions? multipleMutationOptions = null;

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
                            MultipleCreateOptions? multipleCreateOptions = _multipleCreateOptionsConverter.Read(ref reader, typeToConvert, options);
                            if (multipleCreateOptions is not null)
                            {
                                multipleMutationOptions = new(multipleCreateOptions);
                            }

                            break;

                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }

                return multipleMutationOptions;
            }

            throw new JsonException("Failed to read the GraphQL Multiple Mutation options");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, MultipleMutationOptions? value, JsonSerializerOptions options)
        {
            // If the multiple mutation options is null, it is not written to the config file.
            if (value is null)
            {
                return;
            }

            writer.WritePropertyName("multiple-mutations");

            writer.WriteStartObject();

            if (value.MultipleCreateOptions is not null)
            {
                _multipleCreateOptionsConverter.Write(writer, value.MultipleCreateOptions, options);
            }

            writer.WriteEndObject();
        }
    }
}

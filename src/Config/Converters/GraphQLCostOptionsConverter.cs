// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters
{
    /// <summary>
    /// Converter for the GraphQL cost options.
    /// </summary>
    internal class GraphQLCostOptionsConverter : JsonConverter<GraphQLCostOptions>
    {
        /// <inheritdoc/>
        public override GraphQLCostOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is JsonTokenType.StartObject)
            {
                GraphQLCostOptions costOptions = new();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string? propertyName = reader.GetString();

                    if (propertyName is null)
                    {
                        throw new JsonException("Invalid property : null");
                    }

                    reader.Read();
                    switch (propertyName)
                    {
                        case "enabled":
                            if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
                            {
                                costOptions = costOptions with { Enabled = reader.GetBoolean() };
                            }
                            else
                            {
                                throw new JsonException($"Unexpected type of value entered for cost.enabled: {reader.TokenType}");
                            }

                            break;
                        case "max-field-cost":
                            costOptions = costOptions with { MaxFieldCost = ReadCostLimit(ref reader, propertyName) };
                            break;
                        case "max-type-cost":
                            costOptions = costOptions with { MaxTypeCost = ReadCostLimit(ref reader, propertyName) };
                            break;
                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }

                return costOptions;
            }

            throw new JsonException("Failed to read the GraphQL cost options");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, GraphQLCostOptions? value, JsonSerializerOptions options)
        {
            // If the value is null, it is not written to the config file.
            if (value is null)
            {
                return;
            }

            writer.WritePropertyName("cost");

            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value.Enabled);

            if (value.MaxFieldCost is not null)
            {
                writer.WriteNumber("max-field-cost", value.MaxFieldCost.Value);
            }

            if (value.MaxTypeCost is not null)
            {
                writer.WriteNumber("max-type-cost", value.MaxTypeCost.Value);
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads a cost limit (max-field-cost or max-type-cost). A limit must be an integer within [1 to Int32.MaxValue].
        /// </summary>
        private static int? ReadCostLimit(ref Utf8JsonReader reader, string propertyName)
        {
            if (reader.TokenType is JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is not JsonTokenType.Number)
            {
                throw new JsonException($"Unsupported value entered for cost.{propertyName}: {reader.TokenType}");
            }

            int limit;
            try
            {
                limit = reader.GetInt32();
            }
            catch (FormatException)
            {
                throw new JsonException($"The JSON token value is of the incorrect numeric format.");
            }

            if (limit < 1)
            {
                throw new JsonException($"Invalid cost.{propertyName}: {limit}. Specify a value > 0.");
            }

            return limit;
        }
    }
}

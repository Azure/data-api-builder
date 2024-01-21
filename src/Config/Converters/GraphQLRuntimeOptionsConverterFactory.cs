// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class GraphQLRuntimeOptionsConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(GraphQLRuntimeOptions));
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new GraphQLRuntimeOptionsConverter();
    }

    private class GraphQLRuntimeOptionsConverter : JsonConverter<GraphQLRuntimeOptions>
    {
        public override GraphQLRuntimeOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.Null)
            {
                return new GraphQLRuntimeOptions();
            }

            if (reader.TokenType == JsonTokenType.False)
            {
                return new GraphQLRuntimeOptions(Enabled: false);
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                GraphQLRuntimeOptions graphQLRuntimeOptions = new();
                NestedMutationOptionsConverter nestedMutationOptionsConverter = options.GetConverter(typeof(NestedMutationOptions)) as NestedMutationOptionsConverter ??
                                            throw new JsonException("Failed to get nested mutation options converter");

                while (reader.Read())
                {

                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string? propertyName = reader.GetString();
                    reader.Read();
                    switch (propertyName)
                    {
                        case "enabled":
                            if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
                            {
                                graphQLRuntimeOptions = graphQLRuntimeOptions with { Enabled = reader.GetBoolean() };
                            }
                            else
                            {
                                throw new JsonException($"Unexpected type of value entered for enabled: {reader.TokenType}");
                            }

                            break;

                        case "allow-introspection":
                            if (reader.TokenType is JsonTokenType.True || reader.TokenType is JsonTokenType.False)
                            {
                                graphQLRuntimeOptions = graphQLRuntimeOptions with { AllowIntrospection = reader.GetBoolean() };
                            }
                            else
                            {
                                throw new JsonException($"Unexpected type of value entered for allow-introspection: {reader.TokenType}");
                            }

                            break;
                        case "path":
                            if (reader.TokenType is JsonTokenType.String)
                            {
                                string? path = reader.GetString();
                                if (path is null)
                                {
                                    path = "/graphql";
                                }

                                graphQLRuntimeOptions = graphQLRuntimeOptions with { Path = path };
                            }
                            else
                            {
                                throw new JsonException($"Unexpected type of value entered for path: {reader.TokenType}");
                            }

                            break;

                        case "nested-mutations":
                            graphQLRuntimeOptions = graphQLRuntimeOptions with { NestedMutationOptions = nestedMutationOptionsConverter.Read(ref reader, typeToConvert, options) };
                            break;

                        default:
                            throw new JsonException($"Unexpected property {propertyName}");
                    }
                }

                return graphQLRuntimeOptions;
            }

            throw new JsonException("Failed to read the GraphQL Runtime Options");
        }

        public override void Write(Utf8JsonWriter writer, GraphQLRuntimeOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", value.Enabled);
            writer.WriteString("path", value.Path);
            writer.WriteBoolean("allow-introspection", value.AllowIntrospection);

            if (value.NestedMutationOptions is not null)
            {

                NestedMutationOptionsConverter nestedMutationOptionsConverter = options.GetConverter(typeof(NestedMutationOptions)) as NestedMutationOptionsConverter ??
                                            throw new JsonException("Failed to get nested mutation options converter");

                nestedMutationOptionsConverter.Write(writer, value.NestedMutationOptions, options);
            }

            writer.WriteEndObject();
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using System.Text.Json;
using Humanizer;

namespace Azure.DataApiBuilder.Config.Converters;

class RuntimeEntitiesConverter : JsonConverter<RuntimeEntities>
{
    public override RuntimeEntities? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        IDictionary<string, Entity> entities =
            JsonSerializer.Deserialize<Dictionary<string, Entity>>(ref reader, options) ??
            throw new JsonException("Failed to read entities");

        Dictionary<string, Entity> parsedEntities = new();

        foreach ((string key, Entity entity) in entities)
        {
            Entity processedEntity = ProcessGraphQLDefaults(key, entity);

            parsedEntities.Add(key, processedEntity);
        }

        return new RuntimeEntities(parsedEntities);
    }

    /// <summary>
    /// Process the GraphQL defaults for the entity.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="entity">The previously parsed <c>Entity</c> object.</param>
    /// <returns>A processed <c>Entity</c> with default rules applied.</returns>
    private static Entity ProcessGraphQLDefaults(string entityName, Entity entity)
    {
        Entity nameCorrectedEntity = entity;

        // If no GraphQL node was provided in the config, set it with the default state
        if (nameCorrectedEntity.GraphQL is null)
        {
            nameCorrectedEntity = nameCorrectedEntity with { GraphQL = new(Singular: string.Empty, Plural: string.Empty) };
        }

        // If no Singular version of the entity name was provided, use the Entity Name from the config
        if (string.IsNullOrEmpty(nameCorrectedEntity.GraphQL.Singular))
        {
            nameCorrectedEntity = nameCorrectedEntity with { GraphQL = nameCorrectedEntity.GraphQL with { Singular = entityName } };
        }

        // If no Plural version for the entity name was provided, pluralise the singular version.
        if (string.IsNullOrEmpty(nameCorrectedEntity.GraphQL.Plural))
        {
            nameCorrectedEntity = nameCorrectedEntity with { GraphQL = nameCorrectedEntity.GraphQL with { Plural = nameCorrectedEntity.GraphQL.Singular.Pluralize() } };
        }

        return nameCorrectedEntity;
    }

    public override void Write(Utf8JsonWriter writer, RuntimeEntities value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}

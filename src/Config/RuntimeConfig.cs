using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;
using Humanizer;

namespace Azure.DataApiBuilder.Config;

[JsonConverter(typeof(RuntimeEntitiesConverter))]
public record RuntimeEntities : IEnumerable<KeyValuePair<string, Entity>>
{
    public IDictionary<string, Entity> Entities { get; init; }
    public RuntimeEntities(IDictionary<string, Entity> entities)
    {
        Dictionary<string, Entity> parsedEntities = new();

        foreach ((string key, Entity entity) in entities)
        {
            Entity processedEntity = ProcessGraphQLDefaults(key, entity);

            parsedEntities.Add(key, processedEntity);
        }

        Entities = parsedEntities;
    }

    public IEnumerator<KeyValuePair<string, Entity>> GetEnumerator()
    {
        return Entities.GetEnumerator();
    }

    public bool TryGetValue(string key, [NotNullWhen(true)] out Entity? entity)
    {
        return Entities.TryGetValue(key, out entity);
    }

    public bool ContainsKey(string key)
    {
        return Entities.ContainsKey(key);
    }

    public Entity this[string key] => Entities[key];

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
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
            nameCorrectedEntity = nameCorrectedEntity
                with
            { GraphQL = new(Singular: string.Empty, Plural: string.Empty) };
        }

        // If no Singular version of the entity name was provided, use the Entity Name from the config
        if (string.IsNullOrEmpty(nameCorrectedEntity.GraphQL.Singular))
        {
            nameCorrectedEntity = nameCorrectedEntity
                with
            {
                GraphQL = nameCorrectedEntity.GraphQL
            with
                { Singular = entityName }
            };
        }

        // If no Plural version for the entity name was provided, pluralise the singular version.
        if (string.IsNullOrEmpty(nameCorrectedEntity.GraphQL.Plural))
        {
            nameCorrectedEntity = nameCorrectedEntity
                with
            {
                GraphQL = nameCorrectedEntity.GraphQL
                with
                { Plural = nameCorrectedEntity.GraphQL.Singular.Pluralize() }
            };
        }

        // If this is a Stored Procedure with no provided GraphQL operation, set it to Mutation as the default
        if (nameCorrectedEntity.GraphQL.Operation is null && nameCorrectedEntity.Source.Type is EntityType.StoredProcedure)
        {
            nameCorrectedEntity = nameCorrectedEntity
                with
            { GraphQL = nameCorrectedEntity.GraphQL with { Operation = GraphQLOperation.Mutation } };
        }

        // If no Rest node was provided in the config, set it with the default state of enabled for all verbs
        if (nameCorrectedEntity.Rest is null)
        {
            nameCorrectedEntity = nameCorrectedEntity
                with
            { Rest = new EntityRestOptions(EntityRestOptions.DEFAULT_SUPPORTED_VERBS) };
        }

        return nameCorrectedEntity;
    }
}

public record RuntimeConfig(
    string Schema,
    DataSource DataSource,
    RuntimeOptions Runtime,
    RuntimeEntities Entities)
{
    /// <summary>
    /// Serializes the RuntimeConfig object to JSON for writing to file.
    /// </summary>
    /// <returns></returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, RuntimeConfigLoader.GetSerializationOption());
    }
}

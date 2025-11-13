// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents a collection of auto-entity definitions.
/// Each definition is keyed by a unique definition name.
/// </summary>
[JsonConverter(typeof(RuntimeAutoEntitiesConverter))]
public class RuntimeAutoEntities : IEnumerable<KeyValuePair<string, AutoEntity>>
{
    private readonly Dictionary<string, AutoEntity> _autoEntities;

    /// <summary>
    /// Creates a new RuntimeAutoEntities collection.
    /// </summary>
    /// <param name="autoEntities">Dictionary of auto-entity definitions keyed by definition name.</param>
    public RuntimeAutoEntities(Dictionary<string, AutoEntity>? autoEntities = null)
    {
        _autoEntities = autoEntities ?? new Dictionary<string, AutoEntity>();
    }

    /// <summary>
    /// Gets an auto-entity definition by its definition name.
    /// </summary>
    /// <param name="definitionName">The name of the auto-entity definition.</param>
    /// <returns>The auto-entity definition.</returns>
    public AutoEntity this[string definitionName] => _autoEntities[definitionName];

    /// <summary>
    /// Tries to get an auto-entity definition by its definition name.
    /// </summary>
    /// <param name="definitionName">The name of the auto-entity definition.</param>
    /// <param name="autoEntity">The auto-entity definition if found.</param>
    /// <returns>True if the auto-entity definition was found, false otherwise.</returns>
    public bool TryGetValue(string definitionName, [NotNullWhen(true)] out AutoEntity? autoEntity)
    {
        return _autoEntities.TryGetValue(definitionName, out autoEntity);
    }

    /// <summary>
    /// Determines whether an auto-entity definition with the specified name exists.
    /// </summary>
    /// <param name="definitionName">The name of the auto-entity definition.</param>
    /// <returns>True if the auto-entity definition exists, false otherwise.</returns>
    public bool ContainsKey(string definitionName)
    {
        return _autoEntities.ContainsKey(definitionName);
    }

    /// <summary>
    /// Gets the number of auto-entity definitions in the collection.
    /// </summary>
    public int Count => _autoEntities.Count;

    /// <summary>
    /// Gets all the auto-entity definition names.
    /// </summary>
    public IEnumerable<string> Keys => _autoEntities.Keys;

    /// <summary>
    /// Gets all the auto-entity definitions.
    /// </summary>
    public IEnumerable<AutoEntity> Values => _autoEntities.Values;

    public IEnumerator<KeyValuePair<string, AutoEntity>> GetEnumerator()
    {
        return _autoEntities.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

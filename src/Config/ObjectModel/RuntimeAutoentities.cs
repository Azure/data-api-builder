// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents a collection of <see cref="Autoentity"/> available from the RuntimeConfig.
/// </summary>
[JsonConverter(typeof(RuntimeAutoentitiesConverter))]
public record RuntimeAutoentities : IEnumerable<KeyValuePair<string, Autoentity>>
{
    /// <summary>
    /// The collection of <see cref="Autoentity"/> available from the RuntimeConfig.
    /// </summary>
    public IReadOnlyDictionary<string, Autoentity> Autoentities { get; init; }

    /// <summary>
    /// Creates a new instance of the <see cref="RuntimeAutoentities"/> class using a collection of entities.
    /// </summary>
    /// <param name="autoentities">The collection of auto-entities to map to RuntimeAutoentities.</param>
    public RuntimeAutoentities(IReadOnlyDictionary<string, Autoentity> autoentities)
    {
        Autoentities = autoentities;
    }

    public IEnumerator<KeyValuePair<string, Autoentity>> GetEnumerator()
    {
        return Autoentities.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

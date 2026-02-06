// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the configuration options for Azure Managed Redis for semantic caching.
/// </summary>
public record AzureManagedRedisOptions
{
    /// <summary>
    /// Connection string for Azure Managed Redis.
    /// Recommended to inject via environment variable.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Name of the Redis vector index.
    /// </summary>
    public string? VectorIndex { get; init; }

    /// <summary>
    /// Optional Redis key prefix for cache entries.
    /// </summary>
    public string? KeyPrefix { get; init; }

    [JsonConstructor]
    public AzureManagedRedisOptions(
        string? connectionString = null,
        string? vectorIndex = null,
        string? keyPrefix = null)
    {
        if (connectionString is not null)
        {
            ConnectionString = connectionString;
            UserProvidedConnectionString = true;
        }

        if (vectorIndex is not null)
        {
            VectorIndex = vectorIndex;
            UserProvidedVectorIndex = true;
        }

        if (keyPrefix is not null)
        {
            KeyPrefix = keyPrefix;
            UserProvidedKeyPrefix = true;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write connection-string
    /// property and value to the runtime config file.
    /// When user doesn't provide the connection-string property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ConnectionString))]
    public bool UserProvidedConnectionString { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write vector-index
    /// property and value to the runtime config file.
    /// When user doesn't provide the vector-index property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(VectorIndex))]
    public bool UserProvidedVectorIndex { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write key-prefix
    /// property and value to the runtime config file.
    /// When user doesn't provide the key-prefix property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(KeyPrefix))]
    public bool UserProvidedKeyPrefix { get; init; } = false;
}

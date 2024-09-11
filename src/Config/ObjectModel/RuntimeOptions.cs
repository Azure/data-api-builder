// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeOptions
{
    public RestRuntimeOptions? Rest { get; init; }
    public GraphQLRuntimeOptions? GraphQL { get; init; }
    public HostOptions? Host { get; init; }
    public string? BaseRoute { get; init; }
    public TelemetryOptions? Telemetry { get; init; }
    public EntityCacheOptions? Cache { get; init; }
    public PaginationOptions? Pagination { get; init; }

    [JsonPropertyName("log-level")]
    public LogLevelOptions? LoggerLevel { get; init; }

    [JsonConstructor]
    public RuntimeOptions(
        RestRuntimeOptions? Rest,
        GraphQLRuntimeOptions? GraphQL,
        HostOptions? Host,
        string? BaseRoute = null,
        TelemetryOptions? Telemetry = null,
        EntityCacheOptions? Cache = null,
        PaginationOptions? Pagination = null,
        LogLevelOptions? LoggerLevel = null)
    {
        this.Rest = Rest;
        this.GraphQL = GraphQL;
        this.Host = Host;
        this.BaseRoute = BaseRoute;
        this.Telemetry = Telemetry;
        this.Cache = Cache;
        this.Pagination = Pagination;
        this.LoggerLevel = LoggerLevel;
    }

    /// <summary>
    /// Resolves the value of the cache property if present, default is false.
    /// Caching is enabled only when explicitly set to true.
    /// </summary>
    /// <returns>Whether caching is enabled globally.</returns>
    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(Cache))]
    public bool IsCachingEnabled =>
            Cache is not null &&
            Cache.Enabled is not null &&
            Cache.Enabled is true;
}

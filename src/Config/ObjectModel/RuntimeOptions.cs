// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeOptions
{
    public RestRuntimeOptions? Rest { get; init; }
    public GraphQLRuntimeOptions? GraphQL { get; init; }
    public HostOptions? Host { get; set; }
    public string? BaseRoute { get; init; }
    public TelemetryOptions? Telemetry { get; init; }
    public RuntimeCacheOptions? Cache { get; init; }
    public PaginationOptions? Pagination { get; init; }
    public RuntimeHealthCheckConfig? Health { get; init; }

    [JsonConstructor]
    public RuntimeOptions(
        RestRuntimeOptions? Rest,
        GraphQLRuntimeOptions? GraphQL,
        HostOptions? Host,
        string? BaseRoute = null,
        TelemetryOptions? Telemetry = null,
        RuntimeCacheOptions? Cache = null,
        PaginationOptions? Pagination = null,
        RuntimeHealthCheckConfig? Health = null)
    {
        this.Rest = Rest;
        this.GraphQL = GraphQL;
        this.Host = Host;
        this.BaseRoute = BaseRoute;
        this.Telemetry = Telemetry;
        this.Cache = Cache;
        this.Pagination = Pagination;
        this.Health = Health;
    }

    /// <summary>
    /// Resolves the value of the cache property if present, default is false.
    /// Caching is enabled only when explicitly set to true.
    /// </summary>
    /// <returns>Whether caching is enabled globally.</returns>
    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(Cache))]
    public bool IsCachingEnabled => Cache?.Enabled is true;

    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(Rest))]
    public bool IsRestEnabled =>
        Rest is null ||
        Rest?.Enabled is null ||
        Rest?.Enabled is true;

    [JsonIgnore]
    public bool IsGraphQLEnabled =>
        GraphQL is null ||
        GraphQL?.Enabled is null ||
        GraphQL?.Enabled is true;

    [JsonIgnore]
    public bool IsHealthCheckEnabled =>
        Health is null ||
        Health?.Enabled is null ||
        Health?.Enabled is true;
}

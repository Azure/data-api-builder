// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeOptions(
    RestRuntimeOptions? Rest,
    GraphQLRuntimeOptions? GraphQL,
    HostOptions? Host,
    string? BaseRoute = null,
    TelemetryOptions? Telemetry = null,
    [property: JsonPropertyName("cache")] bool? CacheEnabled = null);

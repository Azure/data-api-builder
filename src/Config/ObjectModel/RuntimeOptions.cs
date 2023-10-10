// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeOptions(
    RestRuntimeOptions Rest,
    GraphQLRuntimeOptions GraphQL,
    HostOptions Host,
    string? BaseRoute = null,
    TelemetryOptions? Telemetry = null);

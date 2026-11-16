// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.Telemetry;

/// <summary>
/// Closed lifecycle boundaries. These describe where a failure was observed, never its
/// exception type, text, resource name or configuration contents.
/// </summary>
internal enum TelemetryFailureStage
{
    Unknown,
    Initialization,
    Configuration,
    Parsing,
    Validation,
    Metadata,
    Serving
}

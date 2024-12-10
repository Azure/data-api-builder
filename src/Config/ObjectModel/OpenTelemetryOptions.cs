// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using OpenTelemetry.Exporter;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring Open Telemetry.
/// </summary>
public record OpenTelemetryOptions(bool Enabled = false, string? Endpoint = null, string? Headers = null, OtlpExportProtocol? ExporterProtocol = null, string? ServiceName = null)
{ }

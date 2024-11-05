#if NET8_0_OR_GREATER
using OpenTelemetry.Exporter;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring Open Telemetry.
/// </summary>
public record OpenTelemetryOptions(bool Enabled = false, string? Endpoint = null, string? Headers = null, OtlpExportProtocol? OtlpExportProtocol = null, string? ServiceName = null)
{ }
#endif

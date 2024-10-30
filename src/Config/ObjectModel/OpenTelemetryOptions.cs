namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring Open Telemetry.
/// </summary>
public record OpenTelemetryOptions(bool Enabled = false, string? Endpoint = null, string? Headers = null, string? ServiceName = null)
{ }

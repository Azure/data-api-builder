// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Mcp.Health;

public record CheckResult(string Name, bool IsHealthy, string? Message, Dictionary<string, string> Tags)
{
    public string Status => IsHealthy ? "Healthy" : "Unhealthy";

    public object ToReport()
    {
        return IsHealthy ? new
        {
            Name,
            Status,
            Tags
        } : new
        {
            Name,
            Status,
            Tags,
            Message
        };
    }
}

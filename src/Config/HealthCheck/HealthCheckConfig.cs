// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record HealthCheckConfig
{
    public bool Enabled { get; set; } = true; // Default value: true
}

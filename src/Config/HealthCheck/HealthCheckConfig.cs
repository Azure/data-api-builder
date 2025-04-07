// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record HealthCheckConfig
{
    public bool Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedEnabled { get; init; } = false;

    public HealthCheckConfig()
    {
        Enabled = true;
    }

    public HealthCheckConfig(bool? Enabled)
    {
        if (Enabled is not null)
        {
            this.Enabled = (bool)Enabled;
            UserProvidedEnabled = true;
        }
        else
        {
            this.Enabled = true;
        }
    }
}

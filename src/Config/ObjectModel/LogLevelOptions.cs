// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record LogLevelOptions
{
    [JsonPropertyName("level")]
    public Level? Value { get; set; }

    public LogLevelOptions(Level? Value = null)
    {
        this.Value = Value;
    }
}

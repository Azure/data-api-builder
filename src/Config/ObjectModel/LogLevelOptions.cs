// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record LogLevelOptions
{
    [JsonPropertyName("level")]
    public ExtendedLogLevel? Value { get; set; }

    public LogLevelOptions(ExtendedLogLevel? Value = null)
    {
        this.Value = Value;
    }
}

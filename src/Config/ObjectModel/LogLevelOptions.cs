// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Holds the settings used at runtime to set the LogLevel of the different providers
/// </summary>
public record LogLevelOptions
{
    [JsonPropertyName("level")]
    public LogLevel? Value { get; set; }

    public LogLevelOptions(LogLevel? Value = null)
    {
        this.Value = Value;
    }
}

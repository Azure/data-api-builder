// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Specifies the compression level for HTTP response compression.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompressionLevel
{
    /// <summary>
    /// Provides the best compression ratio at the cost of speed.
    /// </summary>
    Optimal,

    /// <summary>
    /// Provides the fastest compression at the cost of compression ratio.
    /// </summary>
    Fastest,

    /// <summary>
    /// Disables compression.
    /// </summary>
    None
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum McpProtocol
{
    /// <summary>
    /// HTTP transport protocol for MCP communication.
    /// </summary>
    Http,

    /// <summary>
    /// Standard I/O transport protocol for MCP communication.
    /// </summary>
    Stdio
}
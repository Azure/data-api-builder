// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Holds the Model Context Protocol (MCP) settings used at runtime.
/// </summary>
/// <param name="Enabled">If the MCP endpoint is enabled.</param>
/// <param name="Path">The URL path at which the MCP endpoint will be exposed.</param>
/// <param name="Protocol">The transport protocol for MCP communication.</param>
public record McpOptions(
    bool Enabled = false,
    string Path = McpOptions.DEFAULT_PATH,
    McpProtocol Protocol = McpProtocol.Http)
{
    public const string DEFAULT_PATH = "/mcp";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = Enabled;

    [JsonPropertyName("path")]
    public string Path { get; init; } = Path;

    [JsonPropertyName("protocol")]
    public McpProtocol Protocol { get; init; } = Protocol;
}
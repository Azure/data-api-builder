// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Holds the AI-related settings used at runtime.
/// </summary>
/// <param name="Mcp">The Model Context Protocol settings.</param>
public record AiOptions(McpOptions? Mcp = null)
{
    [JsonPropertyName("mcp")]
    public McpOptions? Mcp { get; init; } = Mcp;
}
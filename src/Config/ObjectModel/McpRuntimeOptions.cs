// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Global MCP endpoint runtime configuration.
/// </summary>
public record McpRuntimeOptions
{
    public const string DEFAULT_PATH = "/mcp";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("dml-tools")]
    [JsonConverter(typeof(McpOptionsConverterFactory))]
    public DmlToolsConfig? DmlTools { get; init; }

    [JsonConstructor]
    public McpRuntimeOptions(
        bool Enabled = false,
        string? Path = null,
        DmlToolsConfig? DmlTools = null)
    {
        this.Enabled = Enabled;
        this.Path = Path ?? DEFAULT_PATH;
        this.DmlTools = DmlTools;
    }
}

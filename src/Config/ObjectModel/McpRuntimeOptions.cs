// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Global MCP endpoint runtime configuration.
/// </summary>
public record McpRuntimeOptions
{
    public McpRuntimeOptions(
        bool Enabled = false,
        string? Path = null,
        McpDmlToolsOptions? DmlTools = null)
    {
        this.Enabled = Enabled;
        this.Path = Path ?? DEFAULT_PATH;
        this.DmlTools = DmlTools ?? new McpDmlToolsOptions();
    }

    public const string DEFAULT_PATH = "/mcp";

    public bool Enabled { get; init; }

    public string Path { get; init; }

    [JsonPropertyName("dml-tools")]
    public McpDmlToolsOptions DmlTools { get; init; }
}

public record McpDmlToolsOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool Enabled { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool DescribeEntities { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool CreateRecord { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool ReadRecord { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UpdateRecord { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool DeleteRecord { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool ExecuteRecord { get; init; } = false;
}

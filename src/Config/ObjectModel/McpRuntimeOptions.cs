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

/// <summary>
/// DML Tools configuration that can be either a boolean or object with individual tool settings
/// </summary>
[JsonConverter(typeof(McpOptionsConverterFactory))]
public record DmlToolsConfig
{
    public bool AllToolsEnabled { get; init; }
    public bool? DescribeEntities { get; init; }
    public bool? CreateRecord { get; init; }
    public bool? ReadRecord { get; init; }
    public bool? UpdateRecord { get; init; }
    public bool? DeleteRecord { get; init; }
    public bool? ExecuteRecord { get; init; }

    /// <summary>
    /// Creates a DmlToolsConfig with all tools enabled/disabled
    /// </summary>
    public static DmlToolsConfig FromBoolean(bool enabled)
    {
        return new DmlToolsConfig
        {
            AllToolsEnabled = enabled,
            DescribeEntities = null,
            CreateRecord = null,
            ReadRecord = null,
            UpdateRecord = null,
            DeleteRecord = null,
            ExecuteRecord = null
        };
    }

    /// <summary>
    /// Checks if a specific tool is enabled
    /// </summary>
    public bool IsToolEnabled(string toolName)
    {
        return toolName switch
        {
            "describe-entities" => DescribeEntities ?? AllToolsEnabled,
            "create-record" => CreateRecord ?? AllToolsEnabled,
            "read-record" => ReadRecord ?? AllToolsEnabled,
            "update-record" => UpdateRecord ?? AllToolsEnabled,
            "delete-record" => DeleteRecord ?? AllToolsEnabled,
            "execute-record" => ExecuteRecord ?? AllToolsEnabled,
            _ => false
        };
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
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
    /// Whether MCP endpoints are enabled
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The path where MCP endpoints will be exposed
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = DEFAULT_PATH;

    /// <summary>
    /// Configuration for DML tools
    /// </summary>
    [JsonPropertyName("dml-tools")]
    [JsonConverter(typeof(DmlToolsConfigConverter))]
    public DmlToolsConfig? DmlTools { get; init; }

    [JsonConstructor]
    public McpRuntimeOptions(
        bool Enabled = true,
        string? Path = null,
        DmlToolsConfig? DmlTools = null)
{
        this.Enabled = Enabled;

        if (Path is not null)
        {
            this.Path = Path;
            UserProvidedPath = true;
        }
        else
        {
            this.Path = DEFAULT_PATH;
        }

        this.DmlTools = DmlTools;
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write path
    /// property and value to the runtime config file.
    /// When user doesn't provide the path property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedPath { get; init; } = false;
}

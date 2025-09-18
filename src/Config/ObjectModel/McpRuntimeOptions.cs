// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Global MCP endpoint runtime configuration.
/// </summary>
public record McpRuntimeOptions
{
    /// <summary>
    /// Default path for MCP endpoint.
    /// </summary>
    public const string DEFAULT_PATH = "/mcp";

    /// <summary>
    /// Whether MCP endpoints is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Path used to access MCP endpoint.
    /// </summary>
    public string Path { get; init; }

    /// <summary>
    /// DML Tools that are enabled for MCP to access.
    /// </summary>
    [JsonPropertyName("dml-tools")]
    public McpDmlToolsOptions DmlTools { get; init; }

    public McpRuntimeOptions(
        bool Enabled = true,
        string? Path = null,
        McpDmlToolsOptions? DmlTools = null)
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

        this.DmlTools = DmlTools ?? new McpDmlToolsOptions();
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write enabled
    /// property and value to the runtime config file.
    /// When user doesn't provide the enabled property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedEnabled { get; init; } = false;

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

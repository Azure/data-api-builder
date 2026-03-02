// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record McpRuntimeOptions
{
    public const string DEFAULT_PATH = "/mcp";
    public const int DEFAULT_QUERY_TIMEOUT_SECONDS = 10;

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

    /// <summary>
    /// Description of the MCP server to be exposed in the initialize response
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Query timeout in seconds for MCP tool operations.
    /// This timeout is applied to database queries executed by MCP tools.
    /// Default: 10 seconds.
    /// </summary>
    [JsonPropertyName("query-timeout")]
    public int? QueryTimeout { get; init; }

    [JsonConstructor]
    public McpRuntimeOptions(
        bool? Enabled = null,
        string? Path = null,
        DmlToolsConfig? DmlTools = null,
        string? Description = null,
        int? QueryTimeout = null)
    {
        this.Enabled = Enabled ?? true;

        if (Path is not null)
        {
            this.Path = Path;
            UserProvidedPath = true;
        }
        else
        {
            this.Path = DEFAULT_PATH;
        }

        // if DmlTools is null, set All tools enabled by default
        if (DmlTools is null)
        {
            // Use Default instead of FromBoolean to avoid setting UserProvided flags
            this.DmlTools = DmlToolsConfig.Default;
        }
        else
        {
            this.DmlTools = DmlTools;
        }

        this.Description = Description;

        if (QueryTimeout is not null)
        {
            this.QueryTimeout = QueryTimeout;
            UserProvidedQueryTimeout = true;
        }
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

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write query-timeout
    /// property and value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedQueryTimeout { get; init; } = false;

    /// <summary>
    /// Gets the effective query timeout in seconds, using the default if not specified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public int EffectiveQueryTimeoutSeconds => QueryTimeout ?? DEFAULT_QUERY_TIMEOUT_SECONDS;
}

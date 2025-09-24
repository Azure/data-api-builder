// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// Runtime configuration options for MCP (Model Context Protocol)
    /// </summary>
    public record McpRuntimeOptions
    {
        public const string DEFAULT_PATH = "/mcp";

        /// <summary>
        /// Whether MCP endpoints are enabled
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// The path where MCP endpoints will be exposed
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; init; } = DEFAULT_PATH;

        /// <summary>
        /// Configuration for DML tools
        /// </summary>
        [JsonPropertyName("dml-tools")]
        [JsonConverter(typeof(DmlToolsConfigConverterFactory))]
        public DmlToolsConfig? DmlTools { get; init; }

        /// <summary>
        /// Default parameterless constructor
        /// </summary>
        public McpRuntimeOptions()
        {
        }

        /// <summary>
        /// Constructor with all parameters
        /// </summary>
        [JsonConstructor]
        public McpRuntimeOptions(
            bool Enabled = true,
            string? Path = null,
            DmlToolsConfig? DmlTools = null)
        {
            this.Enabled = Enabled;
            this.Path = Path ?? DEFAULT_PATH;
            this.DmlTools = DmlTools;
        }

        /// <summary>
        /// Constructor for backward compatibility with two parameters
        /// </summary>
        public McpRuntimeOptions(bool enabled, string? path) : this(Enabled: enabled, Path: path, DmlTools: null)
        {
        }
    }
}

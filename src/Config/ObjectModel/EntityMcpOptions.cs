// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// Options for Model Context Protocol (MCP) tools at the entity level.
    /// </summary>
    public record EntityMcpOptions
    {
        /// <summary>
        /// Indicates whether custom tools are enabled for this entity.
        /// Only applicable for stored procedures.
        /// </summary>
        [JsonPropertyName("custom-tool")]
        public bool? CustomToolEnabled { get; init; } = false;

        /// <summary>
        /// Indicates whether DML tools are enabled for this entity.
        /// </summary>
        [JsonPropertyName("dml-tools")]
        public bool? DmlToolEnabled { get; init; } = true;

        /// <summary>
        /// Flag which informs CLI and JSON serializer whether to write the CustomToolEnabled
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        public bool UserProvidedCustomToolEnabled { get; init; } = false;

        /// <summary>
        /// Flag which informs CLI and JSON serializer whether to write the DmlToolEnabled
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        public bool UserProvidedDmlToolsEnabled { get; init; } = false;

        /// <summary>
        /// Constructor for EntityMcpOptions
        /// </summary>
        /// <param name="customToolEnabled">The custom tool enabled flag.</param>
        /// <param name="dmlToolsEnabled">The DML tools enabled flag.</param>
        public EntityMcpOptions(bool? customToolEnabled, bool? dmlToolsEnabled)
        {
            if (customToolEnabled is not null)
            {
                this.CustomToolEnabled = customToolEnabled;
                this.UserProvidedCustomToolEnabled = true;
            }

            if (dmlToolsEnabled is not null)
            {
                this.DmlToolEnabled = dmlToolsEnabled;
                this.UserProvidedDmlToolsEnabled = true;
            }
            else
            {
                this.DmlToolEnabled = true;
            }
        }
    }
}

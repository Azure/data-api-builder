// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// DML Tools configuration that can be either a boolean or object with individual tool settings
/// </summary>
public record DmlToolsConfig
{
    public bool AllToolsEnabled { get; init; }
    public bool? DescribeEntities { get; init; }
    public bool? CreateEntity { get; init; }
    public bool? ReadEntity { get; init; }
    public bool? UpdateEntity { get; init; }
    public bool? DeleteEntity { get; init; }
    public bool? ExecuteEntity { get; init; }

    /// <summary>
    /// Creates a DmlToolsConfig with all tools enabled/disabled
    /// </summary>
    public static DmlToolsConfig FromBoolean(bool enabled)
    {
        return new DmlToolsConfig
        {
            AllToolsEnabled = enabled,
            DescribeEntities = null,
            CreateEntity = null,
            ReadEntity = null,
            UpdateEntity = null,
            DeleteEntity = null,
            ExecuteEntity = null
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
            "create-entity" => CreateEntity ?? AllToolsEnabled,
            "read-entity" => ReadEntity ?? AllToolsEnabled,
            "update-entity" => UpdateEntity ?? AllToolsEnabled,
            "delete-entity" => DeleteEntity ?? AllToolsEnabled,
            "execute-entity" => ExecuteEntity ?? AllToolsEnabled,
            _ => false
        };
    }
}

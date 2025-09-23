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
    public bool? CreateRecord { get; init; }
    public bool? ReadRecords { get; init; }
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
            ReadRecords = null,
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
            "read-records" => ReadRecords ?? AllToolsEnabled,
            "update-record" => UpdateRecord ?? AllToolsEnabled,
            "delete-record" => DeleteRecord ?? AllToolsEnabled,
            "execute-record" => ExecuteRecord ?? AllToolsEnabled,
            _ => false
        };
    }
}

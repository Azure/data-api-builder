// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// DML Tools configuration that can be either a boolean or object with individual tool settings
/// </summary>
public record DmlToolsConfig
{
    /// <summary>
    /// Default value for all tools when not specified
    /// </summary>
    public const bool DEFAULT_ENABLED = true;

    /// <summary>
    /// Indicates if all tools are enabled/disabled uniformly
    /// </summary>
    public bool AllToolsEnabled { get; init; }

    /// <summary>
    /// Whether describe-entities tool is enabled
    /// </summary>
    public bool? DescribeEntities { get; init; }

    /// <summary>
    /// Whether create-record tool is enabled
    /// </summary>
    public bool? CreateRecord { get; init; }

    /// <summary>
    /// Whether read-records tool is enabled
    /// </summary>
    public bool? ReadRecords { get; init; }

    /// <summary>
    /// Whether update-record tool is enabled
    /// </summary>
    public bool? UpdateRecord { get; init; }

    /// <summary>
    /// Whether delete-record tool is enabled
    /// </summary>
    public bool? DeleteRecord { get; init; }

    /// <summary>
    /// Whether execute-entity tool is enabled
    /// </summary>
    public bool? ExecuteEntity { get; init; }

    [JsonConstructor]
    public DmlToolsConfig(
        bool? allToolsEnabled = null,
        bool? describeEntities = null,
        bool? createRecord = null,
        bool? readRecords = null,
        bool? updateRecord = null,
        bool? deleteRecord = null,
        bool? executeEntity = null)
    {
        if (allToolsEnabled is not null)
        {
            AllToolsEnabled = allToolsEnabled.Value;
            UserProvidedAllToolsEnabled = true;
        }
        else
        {
            AllToolsEnabled = DEFAULT_ENABLED;
        }

        if (describeEntities is not null)
        {
            DescribeEntities = describeEntities;
            UserProvidedDescribeEntities = true;
        }

        if (createRecord is not null)
        {
            CreateRecord = createRecord;
            UserProvidedCreateRecord = true;
        }

        if (readRecords is not null)
        {
            ReadRecords = readRecords;
            UserProvidedReadRecords = true;
        }

        if (updateRecord is not null)
        {
            UpdateRecord = updateRecord;
            UserProvidedUpdateRecord = true;
        }

        if (deleteRecord is not null)
        {
            DeleteRecord = deleteRecord;
            UserProvidedDeleteRecord = true;
        }

        if (executeEntity is not null)
        {
            ExecuteEntity = executeEntity;
            UserProvidedExecuteEntity = true;
        }
    }

    /// <summary>
    /// Creates a DmlToolsConfig with all tools set to the same state
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
            ExecuteEntity = null
        };
    }

    /// <summary>
    /// Creates a default DmlToolsConfig with all tools enabled
    /// </summary>
    public static DmlToolsConfig Default => FromBoolean(DEFAULT_ENABLED);

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write all-tools-enabled
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(AllToolsEnabled))]
    public bool UserProvidedAllToolsEnabled { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write describe-entities
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DescribeEntities))]
    public bool UserProvidedDescribeEntities { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write create-record
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(CreateRecord))]
    public bool UserProvidedCreateRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write read-records
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ReadRecords))]
    public bool UserProvidedReadRecords { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write update-record
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(UpdateRecord))]
    public bool UserProvidedUpdateRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write delete-record
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DeleteRecord))]
    public bool UserProvidedDeleteRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write execute-entity
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ExecuteEntity))]
    public bool UserProvidedExecuteEntity { get; init; } = false;
}

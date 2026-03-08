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

    /// <summary>
    /// Whether aggregate-records tool is enabled
    /// </summary>
    public bool? AggregateRecords { get; init; }

    [JsonConstructor]
    public DmlToolsConfig(
        bool? allToolsEnabled = null,
        bool? describeEntities = null,
        bool? createRecord = null,
        bool? readRecords = null,
        bool? updateRecord = null,
        bool? deleteRecord = null,
        bool? executeEntity = null,
        bool? aggregateRecords = null)
    {
        if (allToolsEnabled is not null)
        {
            AllToolsEnabled = allToolsEnabled.Value;
            UserProvidedAllTools = true;

            // When allToolsEnabled is set, use it as the default for all tools
            bool toolDefault = allToolsEnabled.Value;

            DescribeEntities = describeEntities ?? toolDefault;
            CreateRecord = createRecord ?? toolDefault;
            ReadRecords = readRecords ?? toolDefault;
            UpdateRecord = updateRecord ?? toolDefault;
            DeleteRecord = deleteRecord ?? toolDefault;
            ExecuteEntity = executeEntity ?? toolDefault;
            AggregateRecords = aggregateRecords ?? toolDefault;
        }
        else
        {
            AllToolsEnabled = DEFAULT_ENABLED;

            // Set values with defaults
            DescribeEntities = describeEntities ?? DEFAULT_ENABLED;
            CreateRecord = createRecord ?? DEFAULT_ENABLED;
            ReadRecords = readRecords ?? DEFAULT_ENABLED;
            UpdateRecord = updateRecord ?? DEFAULT_ENABLED;
            DeleteRecord = deleteRecord ?? DEFAULT_ENABLED;
            ExecuteEntity = executeEntity ?? DEFAULT_ENABLED;
            AggregateRecords = aggregateRecords ?? DEFAULT_ENABLED;
        }

        // Track user-provided status - only true if the parameter was not null
        UserProvidedDescribeEntities = describeEntities is not null;
        UserProvidedCreateRecord = createRecord is not null;
        UserProvidedReadRecords = readRecords is not null;
        UserProvidedUpdateRecord = updateRecord is not null;
        UserProvidedDeleteRecord = deleteRecord is not null;
        UserProvidedExecuteEntity = executeEntity is not null;
        UserProvidedAggregateRecords = aggregateRecords is not null;
    }

    /// <summary>
    /// Creates a DmlToolsConfig with all tools set to the same state
    /// Used when user explicitly sets "dml-tools": true/false
    /// </summary>
    public static DmlToolsConfig FromBoolean(bool enabled)
    {
        // Only pass allToolsEnabled, leave individual tools as null
        return new DmlToolsConfig(
            allToolsEnabled: enabled,
            describeEntities: null,
            createRecord: null,
            readRecords: null,
            updateRecord: null,
            deleteRecord: null,
            executeEntity: null,
            aggregateRecords: null
        );
    }

    /// <summary>
    /// Creates a default DmlToolsConfig with all tools enabled
    /// Used when dml-tools is not specified in config at all
    /// </summary>
    public static DmlToolsConfig Default => new(
        allToolsEnabled: null,
        describeEntities: null,
        createRecord: null,
        readRecords: null,
        updateRecord: null,
        deleteRecord: null,
        executeEntity: null,
        aggregateRecords: null
    );

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write all-tools-enabled
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(AllToolsEnabled))]
    public bool UserProvidedAllTools { get; init; } = false;

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

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write aggregate-records
    /// property/value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(AggregateRecords))]
    public bool UserProvidedAggregateRecords { get; init; } = false;
}

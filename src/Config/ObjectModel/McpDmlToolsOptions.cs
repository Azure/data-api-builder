// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// DML Tools for general CRUD operations on configured entities
/// </summary>
public record McpDmlToolsOptions
{
    public bool DescribeEntities { get; init; }

    public bool CreateRecord { get; init; }

    public bool ReadRecords { get; init; }

    public bool UpdateRecord { get; init; }

    public bool DeleteRecord { get; init; }

    public bool ExecuteRecord { get; init; }

    public McpDmlToolsOptions(
        bool? DescribeEntities = null,
        bool? CreateRecord = null,
        bool? ReadRecords = null,
        bool? UpdateRecord = null,
        bool? DeleteRecord = null,
        bool? ExecuteRecord = null)
    {
        if (DescribeEntities is not null)
        {
            this.DescribeEntities = (bool)DescribeEntities;
            UserProvidedDescribeEntities = true;
        }
        else
        {
            this.DescribeEntities = true;
        }

        if (CreateRecord is not null)
        {
            this.CreateRecord = (bool)CreateRecord;
            UserProvidedCreateRecord = true;
        }
        else
        {
            this.CreateRecord = true;
        }

        if (ReadRecords is not null)
        {
            this.ReadRecords = (bool)ReadRecords;
            UserProvidedReadRecords = true;
        }
        else
        {
            this.ReadRecords = true;
        }

        if (UpdateRecord is not null)
        {
            this.UpdateRecord = (bool)UpdateRecord;
            UserProvidedUpdateRecord = true;
        }
        else
        {
            this.UpdateRecord = true;
        }

        if (DeleteRecord is not null)
        {
            this.DeleteRecord = (bool)DeleteRecord;
            UserProvidedDeleteRecord = true;
        }
        else
        {
            this.DeleteRecord = true;
        }

        if (ExecuteRecord is not null)
        {
            this.ExecuteRecord = (bool)ExecuteRecord;
            UserProvidedExecuteRecord = true;
        }
        else
        {
            this.ExecuteRecord = true;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write describe-entities
    /// property and value to the runtime config file.
    /// When user doesn't provide the describe-entities property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedDescribeEntities { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write create-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the create-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedCreateRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write read-records
    /// property and value to the runtime config file.
    /// When user doesn't provide the read-records property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedReadRecords { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write update-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the update-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedUpdateRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write delete-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the delete-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedDeleteRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write execute-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the execute-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedExecuteRecord { get; init; } = false;
}


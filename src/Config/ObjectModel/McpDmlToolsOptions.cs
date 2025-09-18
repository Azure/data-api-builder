// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// DML Tools found in global MCP configuration.
/// </summary>
public record McpDmlToolsOptions
{
    public bool Enabled { get; init; }

    public bool DescribeEntities { get; init; }

    public bool CreateRecord { get; init; }

    public bool ReadRecord { get; init; }

    public bool UpdateRecord { get; init; }

    public bool DeleteRecord { get; init; }

    public bool ExecuteRecord { get; init; }

    public McpDmlToolsOptions(
        bool? Enabled = null,
        bool? DescribeEntities = null,
        bool? CreateRecord = null,
        bool? ReadRecord = null,
        bool? UpdateRecord = null,
        bool? DeleteRecord = null,
        bool? ExecuteRecord = null)
    {
        if (Enabled is not null)
        {
            this.Enabled = (bool)Enabled;
            UserProvidedEnabled = true;
        }
        else
        {
            this.Enabled = false;
        }

        if (DescribeEntities is not null)
        {
            this.DescribeEntities = (bool)DescribeEntities;
            UserProvidedDescribeEntities = true;
        }
        else
        {
            this.DescribeEntities = false;
        }

        if (CreateRecord is not null)
        {
            this.CreateRecord = (bool)CreateRecord;
            UserProvidedCreateRecord = true;
        }
        else
        {
            this.CreateRecord = false;
        }

        if (ReadRecord is not null)
        {
            this.ReadRecord = (bool)ReadRecord;
            UserProvidedReadRecord = true;
        }
        else
        {
            this.ReadRecord = false;
        }

        if (UpdateRecord is not null)
        {
            this.UpdateRecord = (bool)UpdateRecord;
            UserProvidedUpdateRecord = true;
        }
        else
        {
            this.UpdateRecord = false;
        }

        if (DeleteRecord is not null)
        {
            this.DeleteRecord = (bool)DeleteRecord;
            UserProvidedDeleteRecord = true;
        }
        else
        {
            this.DeleteRecord = false;
        }

        if (ExecuteRecord is not null)
        {
            this.ExecuteRecord = (bool)ExecuteRecord;
            UserProvidedExecuteRecord = true;
        }
        else
        {
            this.ExecuteRecord = false;
        }
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
    /// Flag which informs CLI and JSON serializer whether to write describe-entities
    /// property and value to the runtime config file.
    /// When user doesn't provide the describe-entities property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedDescribeEntities { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write create-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the create-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedCreateRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write read-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the read-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedReadRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write update-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the update-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedUpdateRecord { get; init; } = false;
    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write delete-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the delete-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedDeleteRecord { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write execute-record
    /// property and value to the runtime config file.
    /// When user doesn't provide the execute-record property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedExecuteRecord { get; init; } = false;
}


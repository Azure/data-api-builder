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
    public bool DescribeEntities { get; init; }

    public bool CreateEntity { get; init; }

    public bool ReadEntity { get; init; }

    public bool UpdateEntity { get; init; }

    public bool DeleteEntity { get; init; }

    public bool ExecuteEntity { get; init; }

    public McpDmlToolsOptions(
        bool? DescribeEntities = null,
        bool? CreateEntity = null,
        bool? ReadEntity = null,
        bool? UpdateEntity = null,
        bool? DeleteEntity = null,
        bool? ExecuteEntity = null)
    {
        if (DescribeEntities is not null)
        {
            this.DescribeEntities = (bool)DescribeEntities;
            UserProvidedDescribeEntities = true;
        }
        else
        {
            this.DescribeEntities = false;
        }

        if (CreateEntity is not null)
        {
            this.CreateEntity = (bool)CreateEntity;
            UserProvidedCreateEntity = true;
        }
        else
        {
            this.CreateEntity = false;
        }

        if (ReadEntity is not null)
        {
            this.ReadEntity = (bool)ReadEntity;
            UserProvidedReadEntity = true;
        }
        else
        {
            this.ReadEntity = false;
        }

        if (UpdateEntity is not null)
        {
            this.UpdateEntity = (bool)UpdateEntity;
            UserProvidedUpdateEntity = true;
        }
        else
        {
            this.UpdateEntity = false;
        }

        if (DeleteEntity is not null)
        {
            this.DeleteEntity = (bool)DeleteEntity;
            UserProvidedDeleteEntity = true;
        }
        else
        {
            this.DeleteEntity = false;
        }

        if (ExecuteEntity is not null)
        {
            this.ExecuteEntity = (bool)ExecuteEntity;
            UserProvidedExecuteEntity = true;
        }
        else
        {
            this.ExecuteEntity = false;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write describe-entities
    /// property and value to the runtime config file.
    /// When user doesn't provide the describe-entities property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DescribeEntities))]
    public bool UserProvidedDescribeEntities { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write create-entity
    /// property and value to the runtime config file.
    /// When user doesn't provide the create-entity property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(CreateEntity))]
    public bool UserProvidedCreateEntity { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write read-entity
    /// property and value to the runtime config file.
    /// When user doesn't provide the read-entity property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ReadEntity))]
    public bool UserProvidedReadEntity { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write update-entity
    /// property and value to the runtime config file.
    /// When user doesn't provide the update-entity property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(UpdateEntity))]
    public bool UserProvidedUpdateEntity { get; init; } = false;
    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write delete-entity
    /// property and value to the runtime config file.
    /// When user doesn't provide the delete-entity property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DeleteEntity))]
    public bool UserProvidedDeleteEntity { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write execute-entity
    /// property and value to the runtime config file.
    /// When user doesn't provide the execute-entity property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ExecuteEntity))]
    public bool UserProvidedExecuteEntity { get; init; } = false;
}


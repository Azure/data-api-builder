// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the authentication options for Azure Log Analytics.
/// </summary>
public record AzureLogAnalyticsAuthOptions
{
    /// <summary>
    /// Whether Azure Log Analytics is enabled.
    /// </summary>
    public string? WorkspaceId { get; init; }

    /// <summary>
    /// Authentication options for Azure Log Analytics.
    /// </summary>
    public string? DcrImmutableId { get; init; }

    /// <summary>
    /// Custom log table name in Log Analytics.
    /// </summary>
    public string? DceEndpoint { get; init; }

    [JsonConstructor]
    public AzureLogAnalyticsAuthOptions(string? WorkspaceId = null, string? DcrImmutableId = null, string? DceEndpoint = null)
    {
        if (WorkspaceId is not null)
        {
            this.WorkspaceId = WorkspaceId;
            UserProvidedWorkspaceId = true;
        }

        if (DcrImmutableId is not null)
        {
            this.DcrImmutableId = DcrImmutableId;
            UserProvidedDcrImmutableId = true;
        }

        if (DceEndpoint is not null)
        {
            this.DceEndpoint = DceEndpoint;
            UserProvidedDceEndpoint = true;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write workspace-id
    /// property and value to the runtime config file.
    /// When user doesn't provide the workspace-id property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(WorkspaceId))]
    public bool UserProvidedWorkspaceId { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write dcr-immutable-id
    /// property and value to the runtime config file.
    /// When user doesn't provide the dcr-immutable-id property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DcrImmutableId))]
    public bool UserProvidedDcrImmutableId { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write dce-endpoint
    /// property and value to the runtime config file.
    /// When user doesn't provide the dce-endpoint property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DceEndpoint))]
    public bool UserProvidedDceEndpoint { get; init; } = false;
}

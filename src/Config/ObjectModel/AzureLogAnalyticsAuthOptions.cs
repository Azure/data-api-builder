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
    public string? CustomTableName { get; init; }

    /// <summary>
    /// Authentication options for Azure Log Analytics.
    /// </summary>
    public string? DcrImmutableId { get; init; }

    /// <summary>
    /// Custom log table name in Log Analytics.
    /// </summary>
    public string? DceEndpoint { get; init; }

    [JsonConstructor]
    public AzureLogAnalyticsAuthOptions(string? customTableName = null, string? dcrImmutableId = null, string? dceEndpoint = null)
    {
        if (customTableName is not null)
        {
            CustomTableName = customTableName;
            UserProvidedCustomTableName = true;
        }

        if (dcrImmutableId is not null)
        {
            DcrImmutableId = dcrImmutableId;
            UserProvidedDcrImmutableId = true;
        }

        if (dceEndpoint is not null)
        {
            DceEndpoint = dceEndpoint;
            UserProvidedDceEndpoint = true;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write workspace-id
    /// property and value to the runtime config file.
    /// When user doesn't provide the custom-table-name property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(CustomTableName))]
    public bool UserProvidedCustomTableName { get; init; } = false;

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

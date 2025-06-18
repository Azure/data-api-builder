// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the authentication options for Azure Log Analytics.
/// </summary>
public record AzureLogAnalyticsAuthOptions(
    [property: JsonPropertyName("workspace-id")] string WorkspaceId,
    [property: JsonPropertyName("dcr-immutable-id")] string? DcrImmutableId = null,
    [property: JsonPropertyName("dce-endpoint")] string? DceEndpoint = null)
{ }

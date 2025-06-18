// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the authentication options for Azure Log Analytics.
/// </summary>
public record AzureLogAnalyticsAuthOptions(string? WorkspaceId = null, string? DcrImmutableId = null, string? DceEndpoint = null)
{ }

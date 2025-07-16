// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record AzureKeyVaultOptions
{
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("retry-policy")]
    public AKVRetryPolicyOptions? RetryPolicy { get; init; }
}

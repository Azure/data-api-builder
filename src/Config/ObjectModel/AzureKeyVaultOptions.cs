// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record AzureKeyVaultOptions
{
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("retry-policy")]
    public AKVRetryPolicyOptions? RetryPolicy { get; init; }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write endpoint
    /// property and value to the runtime config file.
    /// When user doesn't provide the endpoint property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Endpoint))]
    public bool UserProvidedEndpoint { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write retry-policy
    /// property and value to the runtime config file.
    /// When user doesn't provide the retry-policy property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(RetryPolicy))]
    public bool UserProvidedRetryPolicy { get; init; } = false;

    [JsonConstructor]
    public AzureKeyVaultOptions(string? endpoint = null, AKVRetryPolicyOptions? retryPolicy = null)
    {
        if (endpoint is not null)
        {
            Endpoint = endpoint;
            UserProvidedEndpoint = true;
        }

        if (retryPolicy is not null)
        {
            RetryPolicy = retryPolicy;
            UserProvidedRetryPolicy = true;
        }
    }
}

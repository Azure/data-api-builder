// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Azure.DataApiBuilder.Auth;

/// <summary>
/// A database authorization policy whose claim references have been replaced by
/// OData parameter aliases. Claim values remain separate from the policy text so
/// they can be injected into the parsed OData AST as typed constants.
/// </summary>
public sealed record ResolvedDatabasePolicy
{
    /// <summary>
    /// Initializes a resolved database policy and takes an immutable snapshot of its claim values.
    /// </summary>
    /// <param name="policy">Policy text containing OData parameter aliases.</param>
    /// <param name="claimValues">Typed claim values keyed by their parameter alias.</param>
    public ResolvedDatabasePolicy(string policy, IReadOnlyDictionary<string, object?> claimValues)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(claimValues);

        Policy = policy;
        ClaimValues = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(claimValues, StringComparer.Ordinal));
    }

    /// <summary>
    /// Policy text containing OData parameter aliases.
    /// </summary>
    public string Policy { get; }

    /// <summary>
    /// Immutable snapshot of typed claim values keyed by parameter alias.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ClaimValues { get; }

    /// <summary>
    /// Represents an operation without a database authorization policy.
    /// </summary>
    public static ResolvedDatabasePolicy Empty { get; } = new(
        string.Empty,
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>()));
}

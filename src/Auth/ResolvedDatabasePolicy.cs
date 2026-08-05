// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Auth;

/// <summary>
/// A database authorization policy whose claim references have been replaced by
/// OData parameter aliases. Claim values remain separate from the policy text so
/// they can be injected into the parsed OData AST as typed constants.
/// </summary>
/// <param name="Policy">Policy text containing OData parameter aliases.</param>
/// <param name="ClaimValues">Typed claim values keyed by their parameter alias.</param>
public sealed record ResolvedDatabasePolicy(
    string Policy,
    IReadOnlyDictionary<string, object?> ClaimValues)
{
    /// <summary>
    /// Represents an operation without a database authorization policy.
    /// </summary>
    public static ResolvedDatabasePolicy Empty { get; } = new(
        string.Empty,
        new Dictionary<string, object?>());
}

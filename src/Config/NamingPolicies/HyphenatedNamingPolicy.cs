// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Config.NamingPolicies;

/// <summary>
/// A <see cref="JsonNamingPolicy"/> that converts PascalCase to hyphenated-case.
///
/// The only exception is the string "graphql", which is converted to "graphql" (lowercase).
/// </summary>
/// <remarks>
/// This is used to simplify how we deserialize the JSON fields of the config file,
/// turning something like <c>data-source</c> to <c>DataSource</c>.
/// </remarks>
/// <example>
/// <code>
///     Input: DataSource
///     Output: data-source
/// </code>
/// </example>
public sealed class HyphenatedNamingPolicy : JsonNamingPolicy
{
    /// <inheritdoc />
    public override string ConvertName(string name)
    {
        return RuntimeConfigLoader.GenerateHyphenatedName(name);
    }
}

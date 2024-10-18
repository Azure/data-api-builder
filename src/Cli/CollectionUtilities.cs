// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli;

/// <summary>
/// A class which contains useful methods for processing collections.
/// Pulled from Microsoft.IdentityModel.JsonWebTokens which changed the
/// helper to be internal.
/// </summary>
/// <seealso cref="https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/CollectionUtilities.cs"/>
internal static class CollectionUtilities
{
    /// <summary>
    /// Checks whether <paramref name="enumerable"/> is null or empty.
    /// </summary>
    /// <typeparam name="T">The type of the <paramref name="enumerable"/>.</typeparam>
    /// <param name="enumerable">The <see cref="IEnumerable{T}"/> to be checked.</param>
    /// <returns>True if <paramref name="enumerable"/> is null or empty, false otherwise.</returns>
    public static bool IsNullOrEmpty<T>(this IEnumerable<T>? enumerable)
    {
        return enumerable == null || !enumerable.Any();
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Claims;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Provides database access tokens acquired using On-Behalf-Of (OBO) flow
/// for user-delegated authentication scenarios.
/// </summary>
public interface IOboTokenProvider
{
    /// <summary>
    /// Acquires a database access token on behalf of the authenticated user.
    /// Uses in-memory caching with early refresh to minimize latency and
    /// avoid expired tokens during active requests.
    /// </summary>
    /// <param name="principal">The authenticated user's claims principal from JWT validation.</param>
    /// <param name="incomingJwtAssertion">The incoming JWT token to use as the OBO assertion.</param>
    /// <param name="databaseAudience">The target database audience (e.g., https://database.windows.net/).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The database access token string, or null if OBO failed.</returns>
    Task<string?> GetAccessTokenOnBehalfOfAsync(
        ClaimsPrincipal principal,
        string incomingJwtAssertion,
        string databaseAudience,
        CancellationToken cancellationToken = default);
}

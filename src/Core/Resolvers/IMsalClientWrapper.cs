// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Identity.Client;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Wrapper interface for MSAL confidential client operations.
/// This abstraction enables unit testing by allowing mocking of MSAL's sealed classes.
/// </summary>
public interface IMsalClientWrapper
{
    /// <summary>
    /// Acquires a token on behalf of a user using the OBO flow.
    /// </summary>
    /// <param name="scopes">The scopes to request.</param>
    /// <param name="userAssertion">The user assertion (incoming JWT).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authentication result containing the access token.</returns>
    Task<AuthenticationResult> AcquireTokenOnBehalfOfAsync(
        string[] scopes,
        string userAssertion,
        CancellationToken cancellationToken = default);
}

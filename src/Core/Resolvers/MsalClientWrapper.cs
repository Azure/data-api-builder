// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Identity.Client;

namespace Azure.DataApiBuilder.Core.Resolvers;

/// <summary>
/// Implementation of <see cref="IMsalClientWrapper"/> that wraps
/// <see cref="IConfidentialClientApplication"/> for OBO token acquisition.
/// </summary>
public sealed class MsalClientWrapper : IMsalClientWrapper
{
    private readonly IConfidentialClientApplication _msalClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsalClientWrapper"/> class.
    /// </summary>
    /// <param name="msalClient">The MSAL confidential client application.</param>
    public MsalClientWrapper(IConfidentialClientApplication msalClient)
    {
        _msalClient = msalClient ?? throw new ArgumentNullException(nameof(msalClient));
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult> AcquireTokenOnBehalfOfAsync(
        string[] scopes,
        string userAssertion,
        CancellationToken cancellationToken = default)
    {
        UserAssertion assertion = new(userAssertion);

        return await _msalClient
            .AcquireTokenOnBehalfOf(scopes, assertion)
            .ExecuteAsync(cancellationToken);
    }
}

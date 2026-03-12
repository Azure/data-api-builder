// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers.UnauthenticatedAuthentication;

/// <summary>
/// This class is used to best integrate with ASP.NET Core AuthenticationHandler base class.
///     Ref: https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Core/src/AuthenticationHandler.cs
/// When "Unauthenticated" is configured, this handler authenticates the user as anonymous,
/// without reading any HTTP authentication headers.
/// </summary>
public class UnauthenticatedAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Constructor for the UnauthenticatedAuthenticationHandler.
    /// Note the parameters are required by the base class.
    /// </summary>
    /// <param name="options">Authentication options.</param>
    /// <param name="logger">Logger factory.</param>
    /// <param name="encoder">URL encoder.</param>
    public UnauthenticatedAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Returns an unauthenticated ClaimsPrincipal for all requests.
    /// The ClaimsPrincipal has no identity and no claims, representing an anonymous user.
    /// </summary>
    /// <returns>An authentication result to ASP.NET Core library authentication mechanisms</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // ClaimsIdentity without authenticationType means the user is not authenticated (anonymous)
        ClaimsIdentity identity = new();
        ClaimsPrincipal claimsPrincipal = new(identity);

        AuthenticationTicket ticket = new(claimsPrincipal, UnauthenticatedAuthenticationDefaults.AUTHENTICATIONSCHEME);
        AuthenticateResult success = AuthenticateResult.Success(ticket);
        return Task.FromResult(success);
    }
}

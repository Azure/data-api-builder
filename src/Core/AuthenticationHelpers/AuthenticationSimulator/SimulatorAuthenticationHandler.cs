// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers.AuthenticationSimulator;

/// <summary>
/// This class is used to best integrate with ASP.NET Core AuthenticationHandler base class.
///     Ref: https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Core/src/AuthenticationHandler.cs
/// When "Simulator" is configured, this handler authenticates the user once per request,
/// and utilizes the base class default handler for
/// - AuthenticateAsync: Authenticates the current request.
/// - Forbid Async: Creates 403 HTTP Response.
/// </summary>
public class SimulatorAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
#if NET8_0_OR_GREATER
    /// <summary>
    /// Constructor for the SimulatorAuthenticationHandler.
    /// Note the parameters are required by the base class.
    /// </summary>
    /// <param name="runtimeConfigProvider">Runtime configuration provider.</param>
    /// <param name="options">Simulator authentication options.</param>
    /// <param name="logger">Logger factory.</param>
    /// <param name="encoder">URL encoder.</param>
    /// <param name="clock">System clock.</param>
    public SimulatorAuthenticationHandler(
        RuntimeConfigProvider runtimeConfigProvider,
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        // ISystemClock is obsolete in .NET 8.0 and later
        // https://learn.microsoft.com/dotnet/core/compatibility/aspnet-core/8.0/isystemclock-obsolete
        : base(options, logger, encoder)
    {
    }
#else
    /// <summary>
    /// Constructor for the SimulatorAuthenticationHandler.
    /// Note the parameters are required by the base class.
    /// </summary>
    /// <param name="runtimeConfigProvider">Runtime configuration provider.</param>
    /// <param name="options">Simulator authentication options.</param>
    /// <param name="logger">Logger factory.</param>
    /// <param name="encoder">URL encoder.</param>
    /// <param name="clock">System clock.</param>
    public SimulatorAuthenticationHandler(
        RuntimeConfigProvider runtimeConfigProvider,
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock) : base(options, logger, encoder, clock)
    {
    }
#endif

    /// <summary>
    /// Gets any authentication data for a request. When a client role header is present,
    /// parses the header and authenticates the user within a ClaimsPrincipal object.
    /// The ClaimsPrincipal is a security principal usable by middleware to identify the
    /// authenticated user.
    /// </summary>
    /// <seealso cref="https://github.com/microsoft/referencesource/blob/master/mscorlib/system/security/claims/ClaimsIdentity.cs"/>
    /// <seealso cref="https://github.com/dotnet/aspnetcore/blob/v6.0.10/src/Http/Authentication.Abstractions/src/AuthenticationTicket.cs"/>
    /// <returns>An authentication result to ASP.NET Core library authentication mechanisms</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // ClaimsIdentity will be authenticated when authenticationType is provided
        ClaimsIdentity identity = new(authenticationType: SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME);

        if (Context.Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out StringValues clientRoleHeader))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, clientRoleHeader.ToString()));
        }

        ClaimsPrincipal claimsPrincipal = new(identity);

        // AuthenticationTicket is Asp.Net Core Abstraction of Authentication information
        // of the authenticated user.
        AuthenticationTicket ticket = new(claimsPrincipal, EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME);
        AuthenticateResult success = AuthenticateResult.Success(ticket);
        return Task.FromResult(success);
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AuthenticationOptions = Azure.DataApiBuilder.Config.ObjectModel.AuthenticationOptions;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// This class is used to best integrate with ASP.NET Core AuthenticationHandler base class.
///     Ref: https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Core/src/AuthenticationHandler.cs
/// When "EasyAuth" is configured, this handler authenticates the user once per request,
/// and utilizes the base class default handler for
/// - AuthenticateAsync: Authenticates the current request.
/// - Forbid Async: Creates 403 HTTP Response.
/// </summary>
public class EasyAuthAuthenticationHandler : AuthenticationHandler<EasyAuthAuthenticationOptions>
{
    /// <summary>
    /// Constructor for the EasyAuthAuthenticationHandler.
    /// Note the parameters are required by the base class.
    /// </summary>
    /// <param name="options">EasyAuth authentication options.</param>
    /// <param name="logger">Logger factory.</param>
    /// <param name="encoder">URL encoder.</param>
    public EasyAuthAuthenticationHandler(
        IOptionsMonitor<EasyAuthAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        // ISystemClock is obsolete in .NET 8.0 and later
        // https://learn.microsoft.com/dotnet/core/compatibility/aspnet-core/8.0/isystemclock-obsolete
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Attempts processing of a request's authentication metadata.
    /// When an EasyAuth header is present, parses the header and authenticates the user within a ClaimsPrincipal object.
    /// The ClaimsPrincipal is a security principal usable by middleware to identify the
    /// authenticated user.
    /// </summary>
    /// <returns>AuthenticatedResult (Fail, NoResult, Success).</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.Request.Headers[AuthenticationOptions.CLIENT_PRINCIPAL_HEADER].Count > 0)
        {
            // 'Options' is hydrated using named options (authentication scheme name) from the internal OptionsMonitor
            // when the base class AuthenticationHandler::InitializeAsync() method
            // is invoked -> which is once for every request.
            // - We shouldn't use OptionsMonitor.CurrentValue.EasyAuthProvider because there exists a default
            // EasyAuthAuthenticationOptions instance implicitly registered in the DI container which is set as "currentValue."
            // The default EasyAuthAuthenticationOptions instance resolves the default EasyAuth enum value StaticWebApps
            // which prevents AppService authentication from working when configured.
            // The OptionsMonitorCache contains two options registrations:
            // 1. Named (the one we want as configured in startup.cs)
            // 2. Unnamed Default (we don't want this one).
            // We could remove the unnamed option from the OptionsMonitorCache by overridding the base class (essentially a NOOP)
            // function InitializeHandlerAsync (aspnetcore issue 57393), though that is unneeded by accessing options as done below.
            // https://github.com/dotnet/aspnetcore/blob/v8.0.10/src/Security/Authentication/Core/src/AuthenticationHandler.cs#L155
            // https://github.com/dotnet/aspnetcore/issues/17539
            // https://github.com/dotnet/aspnetcore/issues/57393#issuecomment-2296992453
            ClaimsIdentity? identity = Options.EasyAuthProvider switch
            {
                EasyAuthType.StaticWebApps => StaticWebAppsAuthentication.Parse(Context, Logger),
                EasyAuthType.AppService => AppServiceAuthentication.Parse(Context, Logger),
                _ => null
            };

            // If identity is null when the X-MS-CLIENT-PRINCIPAL header is present,
            // the header payload failed to parse -> Authentication Failure.
            if (identity is null)
            {
                return Task.FromResult(AuthenticateResult.Fail(failureMessage: EasyAuthAuthenticationDefaults.INVALID_PAYLOAD_ERROR));
            }

            if (HasOnlyAnonymousRole(identity.Claims))
            {
                // When EasyAuth is properly configured, do not terminate the request pipeline
                // since a request is always at least in the anonymous role.
                // This result signals that authentication did not fail, though the request
                // should be evaluated as unauthenticated.
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            ClaimsPrincipal? claimsPrincipal = new(identity);

            if (claimsPrincipal is not null)
            {
                // AuthenticationTicket is Asp.Net Core Abstraction of Authentication information
                // Ref: aspnetcore/src/Http/Authentication.Abstractions/src/AuthenticationTicket.cs 
                AuthenticationTicket ticket = new(claimsPrincipal, EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME);
                AuthenticateResult success = AuthenticateResult.Success(ticket);
                return Task.FromResult(success);
            }
        }

        // The EasyAuth (X-MS-CLIENT-PRINCIPAL) header will only be present in a properly configured environment
        // for authenticated requests and not anonymous requests.
        return Task.FromResult(AuthenticateResult.NoResult());
    }

    /// <summary>
    /// Helper method to check if the only role assigned is the anonymous role.
    /// </summary>
    /// <param name="claims"></param>
    /// <returns></returns>
    public static bool HasOnlyAnonymousRole(IEnumerable<Claim> claims)
    {
        bool isUserAnonymousOnly = false;
        foreach (Claim claim in claims)
        {
            if (claim.Type is AuthenticationOptions.ROLE_CLAIM_TYPE)
            {
                if (claim.Value.Equals(AuthorizationType.Anonymous.ToString(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    isUserAnonymousOnly = true;
                }
                else
                {
                    return false;
                }
            }
        }

        return isUserAnonymousOnly;
    }
}

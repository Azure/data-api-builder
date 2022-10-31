using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
{
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
        /// <param name="clock">System clock.</param>
        public EasyAuthAuthenticationHandler(
            IOptionsMonitor<EasyAuthAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock
            ) : base(options, logger, encoder, clock)
        {
        }

        /// <summary>
        /// Gets any authentication data for a request. When an EasyAuth header is present,
        /// parses the header and authenticates the user within a ClaimsPrincipal object.
        /// The ClaimsPrincipal is a security principal usable by middleware to identify the
        /// authenticated user.
        /// </summary>
        /// <returns>An authentication result to ASP.NET Core library authentication mechanisms</returns>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Context.Request.Headers[AuthenticationConfig.CLIENT_PRINCIPAL_HEADER].Count > 0)
            {
                ClaimsIdentity? identity = Options.EasyAuthProvider switch
                {
                    EasyAuthType.StaticWebApps => StaticWebAppsAuthentication.Parse(Context, Logger),
                    EasyAuthType.AppService => AppServiceAuthentication.Parse(Context, Logger),
                    _ => null
                };

                if (identity is null || HasOnlyAnonymousRole(identity.Claims))
                {
                    // Either the token is invalid, Or the role is only anonymous,
                    // we don't terminate the pipeline since the request is
                    // always at least in the anonymous role.
                    // It means that anything that is exposed anonymously will still be visible.
                    // The role assigned to X-MS-API-ROLE will be anonymous.
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

            // Return no result when no EasyAuth header is present,
            // because a request is always in anonymous role in EasyAuth
            // This scenario is not possible when front loaded with EasyAuth
            // since the X-MS-CLIENT-PRINCIPAL header will always be present in that case.
            // This is applicable when engine is being tested without front loading with EasyAuth.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        /// <summary>
        /// Helper method to check if the only role assigned is the anonymous role.
        /// </summary>
        /// <param name="claims"></param>
        /// <returns></returns>
        private static bool HasOnlyAnonymousRole(IEnumerable<Claim> claims)
        {
            bool isUserAnonymousOnly = false;
            foreach (Claim claim in claims)
            {
                if (claim.Type is AuthenticationConfig.ROLE_CLAIM_TYPE)
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
}

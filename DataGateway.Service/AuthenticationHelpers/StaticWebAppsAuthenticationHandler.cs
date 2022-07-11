using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.AuthenticationHelpers
{
    /// <summary>
    /// This class is used to best integrate with ASP.NET Core AuthenticationHandler base class.
    ///     Ref: https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Core/src/AuthenticationHandler.cs
    /// When "EasyAuth" is configured, this handler authenticates the user once per request,
    /// and utilizes the base class default handler for
    /// - AuthenticateAsync: Authenticates the current request.
    /// - Forbid Async: Creates 403 HTTP Response.
    /// Usage modelled from Microsoft.Identity.Web.
    ///     Ref: https://docs.microsoft.com/en-us/azure/static-web-apps/user-information?tabs=javascript
    /// </summary>
    public class StaticWebAppsAuthenticationHandler : AuthenticationHandler<EasyAuthAuthenticationOptions>
    {
        /// <summary>
        /// Constructor for the EasyAuthAuthenticationHandler.
        /// Note the parameters are required by the base class.
        /// </summary>
        /// <param name="options">Static Web Apps authentication options.</param>
        /// <param name="logger">Logger factory.</param>
        /// <param name="encoder">URL encoder.</param>
        /// <param name="clock">System clock.</param>
        public StaticWebAppsAuthenticationHandler(
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
                ClaimsIdentity? identity = StaticWebAppsAuthentication.Parse(Context.Request);

                if (identity is null)
                {
                    return Task.FromResult(AuthenticateResult.Fail(failureMessage: "Invalid StaticWebApps EasyAuth token."));
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
            // Try another handler
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}

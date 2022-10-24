using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers.AuthenticationSimulator
{
    /// <summary>
    /// This class is used to best integrate with ASP.NET Core AuthenticationHandler base class.
    ///     Ref: https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Core/src/AuthenticationHandler.cs
    /// When "EasyAuth" is configured, this handler authenticates the user once per request,
    /// and utilizes the base class default handler for
    /// - AuthenticateAsync: Authenticates the current request.
    /// - Forbid Async: Creates 403 HTTP Response.
    /// </summary>
    public class SimulatorAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SIMULATOR_PROVIDER = "Simulator";

        /// <summary>
        /// Constructor for the EasyAuthAuthenticationHandler.
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
            ISystemClock clock
            ) : base(options, logger, encoder, clock)
        {
        }

        /// <summary>
        /// Gets any authentication data for a request. When an EasyAuth header is present,
        /// parses the header and authenticates the user within a ClaimsPrincipal object.
        /// The ClaimsPrincipal is a security principal usable by middleware to identify the
        /// authenticated user.
        /// When EasyAuth.AuthenticateDevModeRquests is configured attempt to inject
        /// a bare-bones authenticated user IF the dev mode authenticate-devmode-requests config flag is true
        /// </summary>
        /// <returns>An authentication result to ASP.NET Core library authentication mechanisms</returns>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsIdentity identity = new(authenticationType: SIMULATOR_PROVIDER);
            identity.AddClaim(new Claim(ClaimTypes.Role, AuthorizationResolver.ROLE_ANONYMOUS));
            identity.AddClaim(new Claim(ClaimTypes.Role, AuthorizationResolver.ROLE_AUTHENTICATED));

            if (Context.Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out StringValues clientRoleHeader))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, clientRoleHeader.ToString()));
            }

            ClaimsPrincipal claimsPrincipal = new(identity);

            // AuthenticationTicket is Asp.Net Core Abstraction of Authentication information
            // Ref: aspnetcore/src/Http/Authentication.Abstractions/src/AuthenticationTicket.cs 
            AuthenticationTicket ticket = new(claimsPrincipal, EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME);
            AuthenticateResult success = AuthenticateResult.Success(ticket);
            return Task.FromResult(success);
        }
    }
}

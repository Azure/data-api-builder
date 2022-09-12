using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
{
    /// <summary>
    /// This middleware validates JWT tokens when JWT Auth is configured
    /// and an Authorization HTTP header is present with a token.
    /// This is required since Asp.Net Core UseAuthentication() does not make
    /// AuthZ decisions nor does it terminate requests.
    /// https://github.com/aspnet/Security/issues/1613#issuecomment-358843214
    /// </summary>
    public class AuthenticationMiddleware
    {
        private readonly RequestDelegate _nextMiddleware;
        private readonly RuntimeConfigProvider _runtimeConfigurationProvider;

        public AuthenticationMiddleware(RequestDelegate next, RuntimeConfigProvider runtimeConfigurationProvider)
        {
            _nextMiddleware = next;
            _runtimeConfigurationProvider = runtimeConfigurationProvider;
        }

        /// <summary>
        /// Middleware to authenticate requests where the method
        /// AuthenticateAsync() calls HandleAuthenticateAsync() in one of:
        /// - EasyAuthAuthenticationHandler
        /// - JwtBearerHandler (internal Asp.Net Core class)
        /// A successful result contains validated token data that is
        /// used to retrieve the `identity` from within the Principal in the HttpContext for use
        /// in downstream middleware.
        /// Based on the AuthenticateResult, the clientRoleHeader will be
        /// validated or set depending on DevModAuthenticate flag in the runtime config:
        /// AuthenticateResult: None
        /// 1. DevModeAuthenticate -> Authenticated
        /// 2. All other scenarios -> Anonymous
        /// AuthenticateResult: Succeeded
        /// 1. DevModeAuthenticate -> Authenticated
        /// 2. All other scenarios -> honor client role header
        /// </summary>
        /// <param name="httpContext">Request metadata</param>
        public async Task InvokeAsync(HttpContext httpContext)
        {
            // authNResult will be one of:
            // 1. Succeeded - Authenticated
            // 2. Failure - Token issue
            // 3. None - No token provided, no auth result.
            AuthenticateResult authNResult = await httpContext.AuthenticateAsync();

            // Reject request by terminating the AuthenticationMiddleware
            // when an invalid token is provided and rites challenge response
            // metadata (HTTP 401 Unauthorized response code
            // and www-authenticate headers) to the HTTP Context.
            if (authNResult.Failure is not null)
            {
                IActionResult result = new ChallengeResult();
                await result.ExecuteResultAsync(new ActionContext
                {
                    HttpContext = httpContext
                });

                return;
            }

            // Set the httpContext.user as the authNResult.Principal,
            // which is never null.
            // Only the properties of the Principal.Identity changes depending on the
            // authentication result.
            httpContext.User = authNResult.Principal!;

            string clientDefinedRole = AuthorizationType.Anonymous.ToString();

            // A request can be authenticated in 2 cases:
            // 1. When the request has a valid jwt/easyauth token,
            // 2. When the dev mode authenticate-devmode-requests config flag is true.
            bool isAuthenticatedRequest = authNResult.Succeeded ||
                _runtimeConfigurationProvider.IsAuthenticatedDevModeRequest();

            if (isAuthenticatedRequest)
            {
                clientDefinedRole = AuthorizationType.Authenticated.ToString();
            }

            // Attempt to inject CLIENT_ROLE_HEADER:clientDefinedRole into the httpContext
            // to accomodate client requests that do not include such header.
            // otherwise honor existing CLIENT_ROLE_HEADER:Value
            if (!httpContext.Request.Headers.TryAdd(AuthorizationResolver.CLIENT_ROLE_HEADER, clientDefinedRole))
            {
                // Honor the client role header value already included
                // in an authenticated requests.
                if (isAuthenticatedRequest)
                {
                    clientDefinedRole = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                }
                else
                {
                    // Override existing client role header value for anonymous requests.
                    httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]
                        = clientDefinedRole;
                }
            }

            // Add a role claim to the ClaimsIdentity using the X-MS-API-ROLE header value.
            // Only applicable when the header value matches the system roles
            // Anonymous and Authenticated
            if (clientDefinedRole.Equals(AuthorizationType.Authenticated.ToString(), StringComparison.OrdinalIgnoreCase) ||
                clientDefinedRole.Equals(AuthorizationType.Anonymous.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Claim claim = new(ClaimTypes.Role, clientDefinedRole, ClaimValueTypes.String);

                // To set the IsAuthenticated value as false, omit the authenticationType.
                ClaimsIdentity identity = new();
                identity.AddClaim(claim);
                httpContext.User.AddIdentity(identity);
            }

            await _nextMiddleware(httpContext);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class AuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuthenticationMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuthenticationMiddleware>();
        }
    }
}

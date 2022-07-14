using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.AuthenticationHelpers
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

        public AuthenticationMiddleware(RequestDelegate next)
        {
            _nextMiddleware = next;
        }

        /// <summary>
        /// Explicitly authenticates using JWT authentication scheme.
        /// A successful result contains validated token data that is
        /// used to populate the user object in the HttpContext for use
        /// in downstream middleware.
        /// </summary>
        /// <param name="httpContext"></param>
        public async Task InvokeAsync(HttpContext httpContext)
        {
            // When calling parameterless version of AddAuthentication()
            // the default scheme is used to hydrate the httpContext.User object.
            AuthenticateResult authNResult = await httpContext.AuthenticateAsync();
            httpContext.User = authNResult.Principal!;

            // Check for different scenarios of authentication results.
            if (authNResult.None)
            {
                // The request is to be considered anonymous
                httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationType.Anonymous.ToString().ToLower();
            }
            else if (authNResult.Succeeded)
            {
                // Honor existing role if any, else assign authenticated role.
                httpContext.Request.Headers.TryAdd(AuthorizationResolver.CLIENT_ROLE_HEADER, AuthorizationType.Authenticated.ToString().ToLower());
            }
            else
            {
                // User not being authenticated means validation failed.
                // A challenge result will add WWW-Authenticate header to indicate failure reason
                // Failure reasons: invalid token (specific validation failure).
                // Terminate middleware request pipeline.
                IActionResult result = new ChallengeResult();
                await result.ExecuteResultAsync(new ActionContext
                {
                    HttpContext = httpContext
                });
                return;
            }

            string clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();

            if (clientRoleHeader.Equals(AuthorizationType.Authenticated.ToString(), StringComparison.OrdinalIgnoreCase) ||
                clientRoleHeader.Equals(AuthorizationType.Anonymous.ToString(), StringComparison.OrdinalIgnoreCase))
            {

                //Add a claim for the X-MS-API-ROLE header to the request.
                Claim claim = new(ClaimTypes.Role, clientRoleHeader, ClaimValueTypes.String);

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

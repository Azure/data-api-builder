using System;
using System.Collections.Generic;
using System.Linq;
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

            // Set the httpContext.user as the authNResult.Principal, which is never null.
            // Only the properties of the Principal.Identity changes depending on the
            // authentication result.
            httpContext.User = authNResult.Principal!;

            // Check for different scenarios of authentication results.
            if (authNResult.None)
            {
                // The request is to be considered anonymous
                httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationType.Anonymous.ToString().ToLower();
            }
            else if (authNResult.Succeeded)
            {
                if (IsUserAnonymousOnly(httpContext.User))
                {
                    // If the user is only in anonymous role and gets authenticated,
                    // we terminate the pipeline.
                    await TerminatePipeLine(httpContext);
                    return;
                }
                // Honor existing role if any, else assign authenticated role.
                httpContext.Request.Headers.TryAdd(AuthorizationResolver.CLIENT_ROLE_HEADER, AuthorizationType.Authenticated.ToString().ToLower());
            }
            else
            {
                // User not being authenticated means validation failed.
                // A challenge result will add WWW-Authenticate header to indicate failure reason
                // Failure reasons: invalid token (specific validation failure).
                // Terminate middleware request pipeline.
                await TerminatePipeLine(httpContext);
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

        /// <summary>
        /// Helper method to check if the user is only present in the anonymous role.
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        private static bool IsUserAnonymousOnly(ClaimsPrincipal user)
        {
            bool isUserAnonymousOnly = false;
            foreach(ClaimsIdentity identity in user.Identities)
            {
                foreach(Claim claim in identity.Claims)
                {
                    if (claim.Type is ClaimTypes.Role)
                    {
                        if (claim.Value.Equals(AuthorizationType.Anonymous.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            isUserAnonymousOnly = true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }

            return isUserAnonymousOnly;
        }

        /// <summary>
        /// Method to terminate the middleware pipeline.
        /// </summary>
        /// <param name="httpContext"></param>
        /// <returns></returns>
        private static async Task TerminatePipeLine(HttpContext httpContext)
        {
            IActionResult result = new ChallengeResult();
            await result.ExecuteResultAsync(new ActionContext
            {
                HttpContext = httpContext
            });
            return;
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

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
        /// For EasyAuth, this middleware is triggered after the EasyAuthHandler
        /// validates the token. For JWT authentication scheme, we explicitly authenticate here.
        /// A successful result contains validated token data that is
        /// used to retrieve the `identity` from within the Principal in the HttpContext for use
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

            // A request can be authenticated in 2 cases:
            // 1. When the request has a valid jwt/easyauth token,
            // 2. When in development mode, we want the default state of request as authenticated.
            bool isAuthenticatedRequest = authNResult.Succeeded ||
                _runtimeConfigurationProvider.DoTreatRequestasAuthenticatedInDevelopmentMode();

            string clientRoleHeader = isAuthenticatedRequest
                ? AuthorizationType.Authenticated.ToString()
                : AuthorizationType.Anonymous.ToString();

            // If authN result succeeded, the client role header i.e.
            // X-MS-API-ROLE is set to authenticated (if not already present)
            // otherwise it is either explicitly added to be `anonymous` or
            // its existing value is replaced with anonymous.
            if (!httpContext.Request.Headers.TryAdd(
                    AuthorizationResolver.CLIENT_ROLE_HEADER,
                    clientRoleHeader))
            {
                // if we are unable to add the role, it means it already exists.
                if (isAuthenticatedRequest)
                {
                    // honor and pick up the existing role value.
                    clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
                }
                else
                {
                    // replace its value with anonymous
                    // only when it is NOT in an authenticated scenario.
                    httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]
                        = clientRoleHeader;
                }
            }

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

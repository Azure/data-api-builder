using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
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
        private readonly RuntimeConfig _runtimeConfig;
        private readonly string _jWTAuthHeader = "Authorization";

        public AuthenticationMiddleware(RequestDelegate next, RuntimeConfigProvider runtimeConfigProvider)
        {
            _nextMiddleware = next;
            _runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();
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
            bool useJWTAuth = true;
            if (_runtimeConfig!.AuthNConfig is not null && _runtimeConfig.AuthNConfig.Provider.Equals("EasyAuth"))
            {
                useJWTAuth = false;
            }

            // When calling parameterless version of AddAuthentication()
            // the default scheme is used to hydrate the httpContext.User object.
            AuthenticateResult authNResult = await httpContext.AuthenticateAsync();

            if (authNResult != null)
            {
                httpContext.User = authNResult.Principal!;
            }

            string expectedTokenHeader = useJWTAuth ? _jWTAuthHeader : EasyAuthAuthenticationHandler.EASY_AUTH_HEADER;

            if (expectedTokenHeader.Equals(EasyAuthAuthenticationHandler.EASY_AUTH_HEADER))
            {
                if (authNResult!.None)
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
                    // Authentication failed, terminate the pipeline.
                    await TerminatePipeLine(httpContext);
                    return;
                }
            }
            else
            {
                // User not being authenticated means validation failed.
                // A challenge result will add WWW-Authenticate header to indicate failure reason
                // Failure reasons: no bearer token, invalid token (specific validation failure)
                if (httpContext.Request.Headers.ContainsKey(expectedTokenHeader) && !httpContext.User.Identity!.IsAuthenticated)
                {
                    // Authentication failed, terminate the pipeline.
                    await TerminatePipeLine(httpContext);
                    return;
                }

                if (httpContext.Request.Headers.ContainsKey(expectedTokenHeader))
                {
                    // Honor existing role if any, else assign authenticated role.
                    httpContext.Request.Headers.TryAdd(AuthorizationResolver.CLIENT_ROLE_HEADER, AuthorizationType.Authenticated.ToString().ToLower());
                }
                else
                {
                    // The X-MS-CLIENT-PRINCIPAL header is absent. The request would be considered anonymous.
                    httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationType.Anonymous.ToString().ToLower();
                }
            }

            await _nextMiddleware(httpContext);
        }

        private static async Task TerminatePipeLine(HttpContext httpContext)
        {
            IActionResult result = new ChallengeResult();
            await result.ExecuteResultAsync(new ActionContext
            {
                HttpContext = httpContext
            });

            // Terminate middleware request pipeline
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

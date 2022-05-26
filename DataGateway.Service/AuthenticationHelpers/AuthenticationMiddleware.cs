using System.Threading.Tasks;
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

            if (authNResult != null && authNResult.Succeeded)
            {
                httpContext.User = authNResult.Principal!;
            }

            // User not being authenticated means validation failed.
            // A challenge result will add WWW-Authenticate header to indicate failure reason
            // Failure reasons: no bearer token, invalid token (specific validation failure)
            if (!httpContext.User.Identity!.IsAuthenticated)
            {
                IActionResult result = new ChallengeResult();
                await result.ExecuteResultAsync(new ActionContext
                {
                    HttpContext = httpContext
                });

                // Terminate middleware request pipeline
                return;
            }

            await _nextMiddleware(httpContext);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class JwtAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseJwtAuthenticationMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuthenticationMiddleware>();
        }
    }
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.AuthenticationHelpers
{
    /// <summary>
    /// This middlware validates JWT tokens when EasyAuth is not configured
    /// and an Authorization HTTP header is present with a token.
    /// This is required snce Asp.Net Core UseAuthentication() does not make
    /// AuthZ decisions nor does it terminate requests.
    /// https://github.com/aspnet/Security/issues/1613#issuecomment-358843214
    /// </summary>
    public class JwtAuthenticationMiddleware
    {
        private readonly RequestDelegate _nextMiddleware;
        private const string JWT_AUTH_HEADER = "Authorization";

        public JwtAuthenticationMiddleware(RequestDelegate next)
        {
            _nextMiddleware = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext != null && httpContext.Request.Headers[JWT_AUTH_HEADER].Count > 0)
            {
                // When calling parameterless version of AddAuthentication() with no default scheme,
                // the result of context.AuthenticateAsync(scheme) must be used to populate the context.User object.
                AuthenticateResult authNResult = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

                if (authNResult != null && authNResult.Succeeded)
                {
                    httpContext.User = authNResult.Principal!;
                }

                if (httpContext.Request.Headers[JWT_AUTH_HEADER].Count > 0)
                {
                    // User not being authenticated means validation failed.
                    // A challenge result will add WWW-Authenticate header to indicate failure reason
                    // Failure reasons: no bearer token, invalid token (specific validation failure)
                    if (!httpContext.User.Identity!.IsAuthenticated)
                    {
                        IActionResult result = new ChallengeResult(JwtBearerDefaults.AuthenticationScheme);
                        await result.ExecuteResultAsync(new ActionContext
                        {
                            HttpContext = httpContext
                        });

                        // Terminate middleware request pipeline
                        return;
                    }
                }
            }

            await _nextMiddleware(httpContext!);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class JwtAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseJwtAuthenticationMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JwtAuthenticationMiddleware>();
        }
    }
}

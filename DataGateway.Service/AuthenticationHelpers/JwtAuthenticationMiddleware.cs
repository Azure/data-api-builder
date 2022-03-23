using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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
                await httpContext.AuthenticateAsync(scheme: JwtBearerDefaults.AuthenticationScheme);
                // AuthN Failures (Token Validation issues) should terminate the request with HTTP 401.
                /*if (authNResult.Failure != null)
                {
                    httpContext.Response.StatusCode = 401; //UnAuthorized
                    await httpContext.Response.StartAsync();
                    return;
                }*/
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

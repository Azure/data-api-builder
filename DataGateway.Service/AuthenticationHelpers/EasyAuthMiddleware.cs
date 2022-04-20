using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.AuthenticationHelpers
{
    public class EasyAuthMiddleware
    {
        private readonly RequestDelegate _nextMiddleware;
        private const string EASY_AUTH_HEADER = "X-MS-CLIENT-PRINCIPAL";

        public EasyAuthMiddleware(RequestDelegate next)
        {
            _nextMiddleware = next;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            if (httpContext.Request.Headers[EASY_AUTH_HEADER].Count > 0)
            {
                ClaimsIdentity? identity = EasyAuthAuthentication.Parse(httpContext);

                // Parse EasyAuth injected headers into MiddleWare usable Security Principal
                if (identity == null)
                {
                    identity = EasyAuthAuthentication.Parse(httpContext);
                }

                if (identity != null)
                {
                    httpContext.User = new ClaimsPrincipal(identity);
                }
            }

            // Call the next delegate/middleware in the pipeline.
            await _nextMiddleware(httpContext);
        }
    }

    public static class EasyAuthMiddlewareExtensions
    {
        public static IApplicationBuilder UseEasyAuthMiddleware(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<EasyAuthMiddleware>();
        }
    }
}

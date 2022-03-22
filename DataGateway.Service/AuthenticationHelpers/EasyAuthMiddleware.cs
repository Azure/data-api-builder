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

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Headers[EASY_AUTH_HEADER].Count > 0)
            {
                ClaimsIdentity? identity = AppServiceAuthentication.Parse(context);

                // Parse App Service's EasyAuth injected headers into MiddleWare usable Security Principal
                if (identity == null)
                {
                    identity = AppServiceAuthentication.Parse(context);
                }

                if (identity != null)
                {
                    context.User = new ClaimsPrincipal(identity);
                }
            }

            // Call the next delegate/middleware in the pipeline.
            await _nextMiddleware(context);
        }
    }

    public static class EasyAuthMiddlewareExtensions
    {
        public static IApplicationBuilder UseEasyAuth(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<EasyAuthMiddleware>();
        }
    }
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// This middleware to be executed prior to reaching Controllers
    /// Evaluates request and User(token) claims against developer config permissions.
    /// Authorization should do little to no request validation as that is handled
    /// in later middleware.
    /// </summary>
    public class AuthorizationEngineMiddleware
    {
        private readonly RequestDelegate _next;

        public AuthorizationEngineMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext, IAuthorizationService authorizationService)
        {
            authorizationService.AuthorizeAsync(
                user: httpContext.User,
                resource: null,
                requirements: new[] { new EnforceConfiguredPermissionsRequirement()}
                );

            return _next(httpContext);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class AuthorizationEngineMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuthorizationEngineMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuthorizationEngineMiddleware>();
        }
    }
}

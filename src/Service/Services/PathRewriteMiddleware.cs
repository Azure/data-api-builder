using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Rewrites the first segment of a URL path (/segment1/segment2/segmentN)
    /// to the default GraphQL endpoint value when the first segment
    /// matches the value of the GraphQL endpoint defined in runtime config.
    /// A URL rewrite occurs server-side and is not visible to the client.
    /// The path rewrite middleware allows the engine to honor a custom GraphQL endpoint
    /// because the default mapped GraphQL endpoint cannot be changed after startup.
    /// </summary>
    /// <seealso cref="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/write?view=aspnetcore-6.0"/>
    /// <seealso cref="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/url-rewriting?view=aspnetcore-6.0#url-redirect-and-url-rewrite"/>
    public class PathRewriteMiddleware
    {
        private readonly RequestDelegate _nextMiddleware;
        private readonly RuntimeConfigProvider _runtimeConfigurationProvider;

        // Default configured GraphQL endpoint path used when
        // not defined or customized in runtime configuration.
        private const string DEFAULT_GRAPHQL_PATH = "/graphql";

        /// <summary>
        /// Setup dependencies and requirements for custom middleware.
        /// </summary>
        /// <param name="next">Reference to next middleware in the request pipeline.</param>
        /// <param name="runtimeConfigurationProvider">Runtime configuration provider.</param>
        public PathRewriteMiddleware(RequestDelegate next, RuntimeConfigProvider runtimeConfigurationProvider)
        {
            _nextMiddleware = next;
            _runtimeConfigurationProvider = runtimeConfigurationProvider;
        }

        /// <summary>
        /// Rewrites the first segment of a URL path (/segment1/segment2/segmentN)
        /// to the configured GraphQL endpoint path when the first segment matches
        /// the configured or default GraphQL endpoint path.
        /// Terminates a request with HTTP 404 Not Found when the URL's first segment matches:
        /// the default graphQL path, but the default path is not used (customized).
        /// GraphQL Configured Endpoint: /gql
        /// Request: /gql/.../segmentN | /graphql/.../segmentN
        /// Rewritten Path: /graphql/.../segmentN
        /// GraphQL Configured Endpoint: /graphql
        /// Request: /graphql/.../segmentN | /apiEndpoint/.../segmentN
        /// Rewritten Path: No rewrite
        /// </summary>
        /// <param name="httpContext">Request metadata.</param>
        public async Task InvokeAsync(HttpContext httpContext)
        {
            if (httpContext.Request.Path.HasValue)
            {
                if (TryGetGraphQLRouteFromConfig(out string? graphQLRoute) && graphQLRoute != DEFAULT_GRAPHQL_PATH)
                {
                    if (httpContext.Request.Path.StartsWithSegments(graphQLRoute, comparisonType: StringComparison.OrdinalIgnoreCase, out PathString remaining))
                    {
                        httpContext.Request.Path = new PathString(value: DEFAULT_GRAPHQL_PATH + remaining);
                    }
                    else if (httpContext.Request.Path.StartsWithSegments(DEFAULT_GRAPHQL_PATH, comparisonType: StringComparison.OrdinalIgnoreCase))
                    {
                        httpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        return;
                    }
                }
            }

            await _nextMiddleware(httpContext);
        }

        /// <summary>
        /// When configuration exists and the GraphQL endpoint is enabled,
        /// return the configured GraphQL endpoint path. 
        /// </summary>
        /// <param name="graphQLRoute">The configured GraphQL route path</param>
        /// <returns>True when graphQLRoute is defined, otherwise false.</returns>
        private bool TryGetGraphQLRouteFromConfig([NotNullWhen(true)] out string? graphQLRoute)
        {
            if (_runtimeConfigurationProvider.TryGetRuntimeConfiguration(out RuntimeConfig? config) &&
                config.GraphQLGlobalSettings.Enabled)
            {
                graphQLRoute = config.GraphQLGlobalSettings.Path;
                return true;
            }

            graphQLRoute = null;
            return false;
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class PathRewriteMiddlewareExtensions
    {
        public static IApplicationBuilder UsePathRewriteMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PathRewriteMiddleware>();
        }
    }
}

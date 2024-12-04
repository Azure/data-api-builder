// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Rewrites the first segment of a URL path (/segment1/segment2/segmentN)
    /// to the default GraphQL endpoint value when the first segment
    /// matches the value of the GraphQL endpoint defined in runtime config.
    /// A URL rewrite occurs server-side and is not visible to the client.
    /// The path rewrite middleware allows the engine to honor a custom GraphQL endpoint
    /// because the default mapped GraphQL endpoint cannot be changed after startup, though
    /// requests still need to be directed to the /graphql endpoint as it is explicitly
    /// used to configure HotChocolate
    /// </summary>
    /// <seealso cref="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/write"/>
    /// <seealso cref="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/url-rewriting#url-redirect-and-url-rewrite"/>
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
        /// Request Example 1: /gql/.../segmentN
        /// Request Example 2: /graphql/.../segmentN
        /// Rewritten Path: /graphql/.../segmentN
        /// GraphQL Configured Endpoint: /graphql
        /// Request Example 1: /graphql/.../segmentN | /apiEndpoint/.../segmentN
        /// Rewritten Path: No rewrite
        /// </summary>
        /// <param name="httpContext">Request metadata.</param>
        public async Task InvokeAsync(HttpContext httpContext)
        {
            // If Rest request is made with Rest disabled Globally or graphQL request is made
            // with graphQL disabled globally, then the request will be discarded.
            if (IsEndPointDisabledGlobally(httpContext))
            {
                return;
            }

            // Only attempt to rewrite the URL when the developer configured GraphQL path differs
            // from the internally set default path of /graphql
            if (httpContext.Request.Path.HasValue &&
                TryGetGraphQLRouteFromConfig(out string? graphQLRoute) && graphQLRoute != DEFAULT_GRAPHQL_PATH)
            {
                // Only attempt to rewrite when the request path begins with the developer
                // configured GrahpQL path.
                // When the request path matches the internal /graphql path when a developer
                // configured the path differently, fail the request.
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
            if (_runtimeConfigurationProvider.TryGetLoadedConfig(out RuntimeConfig? config) &&
                config.IsGraphQLEnabled)
            {
                graphQLRoute = config.GraphQLPath;
                return true;
            }

            graphQLRoute = null;
            return false;
        }

        /// <summary>
        /// Sets Http Response code to 404 NOT Found if a REST call is made with REST disabled globally
        /// or if graphql request is made with GraphQL disabled globally.
        /// 404 is also thrown when the request path is invalid.
        /// </summary>
        /// <param name="httpContext">Request metadata.</param>
        /// <returns>True if the given REST/GraphQL request is disabled globally,else false </returns>
        private bool IsEndPointDisabledGlobally(HttpContext httpContext)
        {
            PathString requestPath = httpContext.Request.Path;
            if (_runtimeConfigurationProvider.TryGetLoadedConfig(out RuntimeConfig? config))
            {
                string restPath = config.RestPath;
                string graphQLPath = config.GraphQLPath;
                bool isRestRequest = requestPath.StartsWithSegments(restPath, comparisonType: StringComparison.OrdinalIgnoreCase);
                bool isGraphQLRequest = requestPath.StartsWithSegments(graphQLPath, comparisonType: StringComparison.OrdinalIgnoreCase);

                if ((isRestRequest && !config.IsRestEnabled)
                    || (isGraphQLRequest && !config.IsGraphQLEnabled))
                {
                    httpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    return true;
                }
            }

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

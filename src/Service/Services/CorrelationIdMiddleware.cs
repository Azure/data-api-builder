using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Middleware for tracking correlation id for each request and response.
    /// If none correlation id is passed in through request,
    /// it will generate a new one.
    /// </summary>
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;

        /// <summary>
        /// Setup dependencies and requirements for custom middleware.
        /// </summary>
        /// <param name="next">Reference to next middleware in the request pipeline.</param>
        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        /// <summary>
        /// For a request, check the request headers for correlation id,
        /// if none exists, create a new GUID, and store it in httpContext.items.
        /// For a response, first getting the correlation id value from either http context request or items,
        /// then add it to the response header.
        /// </summary>
        public async Task Invoke(HttpContext httpContext)
        {
            if (!httpContext.Request.Headers.TryGetValue(HttpHeaders.CORRELATION_ID, out StringValues correlationId)
                || !Guid.TryParse(correlationId, out _))
            {
                httpContext.Items.TryAdd(HttpHeaders.CORRELATION_ID, Guid.NewGuid().ToString());
            }

            httpContext.Response.OnStarting(() =>
            {
                Guid? correlationId = httpContext.GetCorrelationId();
                if (correlationId is not null)
                {
                    httpContext.Response.Headers.TryAdd(HttpHeaders.CORRELATION_ID, correlationId.ToString());
                }

                return Task.CompletedTask;
            });
            await _next.Invoke(httpContext);
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class CorrelationIdMiddlewareExtensions
    {
        public static IApplicationBuilder UseCorrelationIdMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CorrelationIdMiddleware>();
        }
    }
}

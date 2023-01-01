using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Services
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (!httpContext.Request.Headers.TryGetValue(HttpHeaders.CORRELATION_ID, out StringValues correlationId)
                && Guid.TryParse(correlationId, out _))
            {
                httpContext.Items.TryAdd(HttpHeaders.CORRELATION_ID, Guid.NewGuid().ToString());
            }

            httpContext.Response.OnStarting(() =>
            {
                string? correlationId = httpContext.GetCorrelationId();

                if (!string.IsNullOrEmpty(correlationId))
                {
                    httpContext.Response.Headers.Add(HttpHeaders.CORRELATION_ID, correlationId);
                }

                return Task.CompletedTask;
            });
            await _next.Invoke(httpContext);
        }
    }
}

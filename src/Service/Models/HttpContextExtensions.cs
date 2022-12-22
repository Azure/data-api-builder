using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Models
{
    public static class HttpContextExtensions
    {
        public static string? GetCorrelationId(this HttpContext context)
        {
            string? correlationId = null;

            if (context.Request.Headers.TryGetValue(HttpHeaders.CORRELATION_ID, out StringValues correlationIdHeader)
                && Guid.TryParse(correlationIdHeader, out _))
            {
                correlationId = correlationIdHeader.ToString();
            }
            else if (context.Items.TryGetValue(HttpHeaders.CORRELATION_ID, out object? correlationIdItem))
            {
                correlationId = correlationIdItem as string;
            }

            return correlationId;
        }
    }
}

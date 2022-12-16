using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualBasic;
using System;

namespace Azure.DataApiBuilder.Service.Models
{
    public static class HttpContextExtensions
    {
        public static string? GetCorrelationId(this HttpContext context)
        {
            string? correlationId = null;

            if (context.Request.Headers.TryGetValue(HttpHeaders.CORRELATION_ID, out StringValues correlationIdHeader))
            {
                correlationId = correlationIdHeader.ToString();
            }
            else if (context.Items.TryGetValue(HttpHeaders.CORRELATION_ID, out object? correlationIdItem))
            {
                correlationId = correlationIdItem as string;
            }

            if (!string.IsNullOrEmpty(correlationId) && !Guid.TryParse(correlationId, out _))
            {
                // Remove correlation id if it's not a valid Guid
                correlationId = null;
            }

            return correlationId;
        }
    }
}

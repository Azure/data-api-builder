using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Models
{
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Retrieving correlation id from http context
        /// </summary>
        /// <param name="context">http context for current request</param>
        /// <returns></returns>
        public static Guid? GetCorrelationId(this HttpContext context)
        {
            Guid correlationId;
            if (context.Request.Headers.TryGetValue(HttpHeaders.CORRELATION_ID, out StringValues correlationIdFromHeader)
                && Guid.TryParse(correlationIdFromHeader, out correlationId))
            {
                return correlationId;
            }

            if (context.Items.TryGetValue(HttpHeaders.CORRELATION_ID, out object? correlationIdItem))
            {
                Guid.TryParse(correlationIdItem as string, out correlationId);
                return correlationId;
            }

            return null;
        }
    }
}

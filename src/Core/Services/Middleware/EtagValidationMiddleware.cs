// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.Services.Middleware;
public class EtagValidationMiddleware
{
    private readonly RequestDelegate _next;

    public EtagValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("If-Match", out StringValues headerValue)
            && !headerValue.Equals("*"))
        {
            var responseObj = new { message = "Etags not supported, use '*'" };
            string? jsonResponse = JsonSerializer.Serialize(responseObj);

            httpContext.Response.ContentType = "application/json";
            httpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await httpContext.Response.WriteAsync(jsonResponse);
            return;
        }

        await _next(httpContext);
    }
}

/// <summary>
/// Extension method used to add the middleware to the HTTP request pipeline.
/// </summary>
public static class ClientRoleHeaderAuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseEtagValidationMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<EtagValidationMiddleware>();
    }
}

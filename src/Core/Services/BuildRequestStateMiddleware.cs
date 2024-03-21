// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Authorization;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using RequestDelegate = HotChocolate.Execution.RequestDelegate;

/// <summary>
/// This request middleware will build up our request state and will be invoked once per request.
/// </summary>
public sealed class BuildRequestStateMiddleware
{
    private readonly RequestDelegate _next;

    public BuildRequestStateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Middleware invocation method which attempts to replicate the
    /// http context's "X-MS-API-ROLE" header/value to HotChocolate's request context.
    /// </summary>
    /// <param name="context">HotChocolate execution request context.</param>
    public async ValueTask InvokeAsync(IRequestContext context)
    {
        if (context.ContextData.TryGetValue(nameof(HttpContext), out object? value) &&
            value is HttpContext httpContext)
        {
            // Because Request.Headers is a NameValueCollection type, key not found will return StringValues.Empty and not an exception.
            StringValues clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
            context.ContextData.TryAdd(key: AuthorizationResolver.CLIENT_ROLE_HEADER, value: clientRoleHeader);
        }

        await _next(context).ConfigureAwait(false);
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Authorization;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using RequestDelegate = HotChocolate.Execution.RequestDelegate;

/// <summary>
/// This request middleware will build up our request state and will be invoke once per request.
/// </summary>
internal sealed class BuildRequestStateMiddleware
{
    private readonly RequestDelegate _next;

    public BuildRequestStateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async ValueTask InvokeAsync(IRequestContext context)
    {
        if (context.ContextData.TryGetValue(nameof(HttpContext), out object? value) &&
            value is HttpContext httpContext)
        {
            StringValues clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
            context.ContextData.TryAdd(key: AuthorizationResolver.CLIENT_ROLE_HEADER, value: clientRoleHeader);
        }

        await _next(context).ConfigureAwait(false);
    }
}

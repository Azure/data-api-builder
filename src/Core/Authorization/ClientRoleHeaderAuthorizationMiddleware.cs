// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.Authorization;

/// <summary>
/// This middleware to be executed prior to reaching Controllers
/// Evaluates request and User(token) claims against developer config permissions.
/// Authorization should do little to no request validation as that is handled
/// in later middleware.
/// </summary>
public class ClientRoleHeaderAuthorizationMiddleware
{
    private readonly RequestDelegate _next;

    public ClientRoleHeaderAuthorizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext httpContext)
    {
        if (!IsValidRoleContext(httpContext))
        {
            //Handle authorization failure and terminate the request.
            httpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        await _next(httpContext);
    }

    public static bool IsValidRoleContext(HttpContext httpContext)
    {
        StringValues clientRoleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];

        if (clientRoleHeader.Count != 1)
        {
            // When count = 0, the clientRoleHeader is absent on requests.
            // Consequentially, anonymous requests must specifically set
            // the clientRoleHeader value to Anonymous.

            // When count > 1, multiple header fields with the same field-name
            // are present in a message, but are NOT supported, specifically for the client role header.
            // Valid scenario per HTTP Spec: http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
            // Discussion: https://stackoverflow.com/a/3097052/18174950
            return false;
        }

        string clientRoleHeaderValue = clientRoleHeader.ToString();

        // The clientRoleHeader must have a value.
        if (clientRoleHeaderValue.Length == 0)
        {
            return false;
        }

        // IsInRole looks at all the claims present in the request
        // Reference: https://github.com/microsoft/referencesource/blob/master/mscorlib/system/security/claims/ClaimsPrincipal.cs
        return httpContext.User.IsInRole(clientRoleHeaderValue);
    }
}

/// <summary>
/// Extension method used to add the middleware to the HTTP request pipeline.
/// </summary>
public static class ClientRoleHeaderAuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseClientRoleHeaderAuthorizationMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ClientRoleHeaderAuthorizationMiddleware>();
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Kestral = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpMethod;
using RequestDelegate = HotChocolate.Execution.RequestDelegate;

/// <summary>
/// This request middleware will build up our request state and will be invoked once per request.
/// </summary>
public sealed class BuildRequestStateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RuntimeConfigProvider _runtimeConfigProvider;

    public BuildRequestStateMiddleware(RequestDelegate next, RuntimeConfigProvider runtimeConfigProvider)
    {
        _next = next;
        _runtimeConfigProvider = runtimeConfigProvider;
    }

    /// <summary>
    /// Middleware invocation method which attempts to replicate the
    /// http context's "X-MS-API-ROLE" header/value to HotChocolate's request context.
    /// </summary>
    /// <param name="context">HotChocolate execution request context.</param>
    public async ValueTask InvokeAsync(RequestContext context)
    {
        bool isIntrospectionQuery = context.Request.OperationName == "IntrospectionQuery";
        ApiType apiType = ApiType.GraphQL;
        Kestral method = Kestral.Post;
        string route = _runtimeConfigProvider.GetConfig().GraphQLPath.Trim('/');
        DefaultHttpContext httpContext = (DefaultHttpContext)context.ContextData.First(x => x.Key == "HttpContext").Value!;
        Stopwatch stopwatch = Stopwatch.StartNew();

        using Activity? activity = !isIntrospectionQuery ?
            TelemetryTracesHelper.DABActivitySource.StartActivity($"{method} /{route}") : null;

        try
        {
            // We want to ignore introspection queries DAB uses to check access to GraphQL since they are not sent by the user.
            if (!isIntrospectionQuery)
            {
                TelemetryMetricsHelper.IncrementActiveRequests(apiType);
                if (activity is not null)
                {
                    activity.TrackMainControllerActivityStarted(
                        httpMethod: method,
                        userAgent: httpContext.Request.Headers["User-Agent"].ToString(),
                        actionType: context.Request.OperationName ?? "GraphQL",
                        httpURL: string.Empty, // GraphQL has no route
                        queryString: null, // GraphQL has no query-string
                        userRole: httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].FirstOrDefault() ?? httpContext.User.FindFirst("role")?.Value,
                        apiType: apiType);
                }
            }

            await InvokeAsync();
        }
        finally
        {
            stopwatch.Stop();

            HttpStatusCode statusCode;

            // We want to ignore introspection queries DAB uses to check access to GraphQL since they are not sent by the user.
            if (!isIntrospectionQuery)
            {
                // There is an error in GraphQL when ContextData is not null
                if (context.Result!.ContextData is not null)
                {
                    if (context.Result.ContextData.ContainsKey(ExecutionContextData.ValidationErrors))
                    {
                        statusCode = HttpStatusCode.BadRequest;
                    }
                    else if (context.Result.ContextData.ContainsKey(ExecutionContextData.OperationNotAllowed))
                    {
                        statusCode = HttpStatusCode.MethodNotAllowed;
                    }
                    else
                    {
                        statusCode = HttpStatusCode.InternalServerError;
                    }

                    Exception ex = new("An error occurred in executing GraphQL operation.");

                    // Activity will track error
                    activity?.TrackMainControllerActivityFinishedWithException(ex, statusCode);
                    TelemetryMetricsHelper.TrackError(method, statusCode, route, apiType, ex);
                }
                else
                {
                    statusCode = HttpStatusCode.OK;
                    activity?.TrackMainControllerActivityFinished(statusCode);
                }

                TelemetryMetricsHelper.TrackRequest(method, statusCode, route, apiType);
                TelemetryMetricsHelper.TrackRequestDuration(method, statusCode, route, apiType, stopwatch.Elapsed);
                TelemetryMetricsHelper.DecrementActiveRequests(apiType);
            }
        }

        async Task InvokeAsync()
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
}


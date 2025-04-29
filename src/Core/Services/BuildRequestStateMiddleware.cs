// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Kestral = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
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
    public async ValueTask InvokeAsync(IRequestContext context)
    {
        (Activity? activity, ApiType apiType, Kestral.HttpMethod method) = StartOuterActivity();
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            TelemetryMetricsHelper.IncrementActiveRequests(apiType);
            await InvokeAsync();
        }
        finally
        {
            if (activity is not null)
            {
                DefaultHttpContext httpContext = (DefaultHttpContext)context.ContextData.First(x => x.Key == "HttpContext").Value!;
                HttpStatusCode statusCode = Enum.Parse<HttpStatusCode>(httpContext.Response.StatusCode.ToString(), ignoreCase: true);

                TelemetryMetricsHelper.TrackRequest(method, statusCode, default!, apiType);
                TelemetryMetricsHelper.TrackRequestDuration(method, statusCode, default!, apiType, stopwatch.Elapsed);
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

        (Activity?, ApiType, Kestral.HttpMethod) StartOuterActivity()
        {
            ApiType apiType = ApiType.GraphQL;
            Kestral.HttpMethod method = Kestral.HttpMethod.Post;
            string route = _runtimeConfigProvider.GetConfig().GraphQLPath.Trim('/');

            using Activity? activity = (context.Request.OperationName != "IntrospectionQuery") ?
                TelemetryTracesHelper.DABActivitySource.StartActivity($"{method} /{route}") : null;

            activity?.TrackRestControllerActivityStarted(
                httpMethod: method,
                userAgent: default!, // TODO: find the real user-agent
                actionType: (context.Request.Query!.ToString().StartsWith("query") ? OperationType.Query : OperationType.Mutation).ToString(),
                httpURL: string.Empty, // GraphQL has no route
                queryString: default!, // GraphQL has no query-string
                userRole: context.ContextData.First(x => x.Key == "X-MS-API-ROLE").Value!.ToString(),
                apiType: apiType);

            return (activity, apiType, method);
        }
    }
}


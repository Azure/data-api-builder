// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Counts REST data requests (including embeddings), not controller invocations. Register after routing and before
    /// authentication so rejected data requests are included. The response, including result
    /// execution and serialization, must finish before a request can be successful.
    /// </summary>
    internal sealed class EngineTelemetryHttpMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RuntimeConfigProvider _configProvider;
        private readonly EngineTelemetrySession _session;

        public EngineTelemetryHttpMiddleware(
            RequestDelegate next,
            RuntimeConfigProvider configProvider,
            EngineTelemetrySession session)
        {
            _next = next;
            _configProvider = configProvider;
            _session = session;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_session.IsEnabled ||
                EngineTelemetryHealthProbe.IsProbe(context, _session) ||
                !_configProvider.TryGetLoadedConfig(out RuntimeConfig? config) ||
                !IsDataRequest(context, config))
            {
                await _next(context);
                return;
            }

            // Begin before doing work: the session captures the accepted configuration epoch.
            // Dispose only restores the ambient scope; it does not record a completion.
            using EngineTelemetryRequestScope request = _session.BeginRequest(
                EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, ClassifyRequestRole(context));
            request.SetOutcome(EngineTelemetryOutcome.Unknown);
            EngineTelemetryHttpCompletion completion = new(
                context, (outcome, status) =>
                {
                    request.SetRole(ClassifyRequestRole(context));
                    request.Complete(outcome, status);
                }, () => request.Outcome, inferSuccessFromHttp: true);

            try
            {
                await _next(context);
            }
            catch (OperationCanceledException)
            {
                completion.Fail(EngineTelemetryOutcome.Canceled);
                throw;
            }
            catch (Exception)
            {
                completion.Fail(context.RequestAborted.IsCancellationRequested
                    ? EngineTelemetryOutcome.Canceled
                    : EngineTelemetryOutcome.Failure);
                throw;
            }
        }

        internal static bool IsDataRequest(HttpContext context, RuntimeConfig config)
        {
            // The standalone embedding endpoint is mapped independently of runtime.rest.
            // Use routing metadata rather than guessing from the URL or inspecting its body.
            if (context.GetEndpoint()?.Metadata.GetMetadata<EngineTelemetryEmbeddingEndpointMetadata>() is not null)
            {
                return HttpMethods.IsPost(context.Request.Method);
            }

            if (!config.IsRestEnabled ||
                !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsPost(context.Request.Method) ||
                  HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method) ||
                  HttpMethods.IsDelete(context.Request.Method)))
            {
                return false;
            }

            // Other controllers own health/bootstrap endpoints. Swagger UI runs before this
            // middleware; do not guess that similarly named entity routes are static assets.
            ControllerActionDescriptor? action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is null || !typeof(RestController).IsAssignableFrom(action.ControllerTypeInfo.AsType()))
            {
                return false;
            }

            // Routing has already selected the controller. A valid REST entity can be below
            // an MCP prefix without being one of that protocol's actual mapped endpoints.
            PathString path = context.Request.Path;
            string restPath = config.RestPath.TrimEnd('/');
            PathString remainder = path;
            if (restPath.Length > 0 &&
                !path.StartsWithSegments(restPath, StringComparison.OrdinalIgnoreCase, out remainder))
            {
                return false;
            }

            // Unlike an arbitrary name such as swagger, openapi is explicitly handled as
            // discovery inside the REST catch-all. Favicon requests are UI discovery too.
            return remainder.HasValue && remainder.Value != "/" &&
                !IsPath(remainder, "/openapi") && !IsPath(remainder, "/favicon.ico");
        }

        internal static EngineTelemetryRole ClassifyRequestRole(HttpContext context)
        {
            bool authenticated = context.User.Identity?.IsAuthenticated == true;
            Microsoft.Extensions.Primitives.StringValues role = context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
            if (role.Count > 1)
            {
                return EngineTelemetryRole.Unknown;
            }

            // This adapter runs before authentication. Credentials are not evidence of a
            // successfully authenticated identity; do not parse tokens or infer their roles.
            if (role.Count == 0 && !authenticated &&
                (context.Request.Headers.ContainsKey("Authorization") ||
                 context.Request.Headers.ContainsKey("X-MS-CLIENT-PRINCIPAL")))
            {
                return EngineTelemetryRole.Unknown;
            }

            return EngineTelemetrySession.ClassifyRole(role.Count == 1 ? role[0] : null, authenticated);
        }

        private static bool IsPath(PathString path, string prefix)
            => !string.IsNullOrEmpty(prefix) && path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase);
    }

}

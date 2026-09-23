// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using GraphQLRequestContext = HotChocolate.Execution.RequestContext;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// One completion per Hot Chocolate execution, including HTTP and variable batches.
    /// Register with AddDiagnosticEventListener, independently of customer tracing listeners.
    /// </summary>
    internal sealed class EngineTelemetryGraphQLListener : ExecutionDiagnosticEventListener
    {
        private const string CONTEXT_KEY = "DAB.ProductTelemetry.GraphQLExecution";
        private readonly EngineTelemetrySession _session;

        public EngineTelemetryGraphQLListener(EngineTelemetrySession session)
        {
            _session = session;
        }

        public override IDisposable ExecuteRequest(GraphQLRequestContext context)
        {
            if (!_session.IsEnabled || context.IsWarmupRequest())
            {
                return EmptyScope;
            }

            HttpContext? httpContext = GetHttpContext(context);
            if (httpContext is not null && EngineTelemetryHealthProbe.IsProbe(httpContext, _session))
            {
                return EmptyScope;
            }

            EngineTelemetryRole role;
            if (httpContext is not null)
            {
                role = EngineTelemetryHttpMiddleware.ClassifyRequestRole(httpContext);
            }
            else
            {
                context.ContextData.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out object? roleValue);
                role = EngineTelemetrySession.ClassifyRole(roleValue is StringValues roles && roles.Count == 1
                    ? roles[0]
                    : roleValue as string);
            }

            // Deliberately synchronous: setting AsyncLocal inside an async helper would not
            // establish it in Hot Chocolate's caller before the resolver tasks are created.
            EngineTelemetryRequestScope request = _session.BeginRequest(
                EngineTelemetryApi.GraphQL,
                httpContext is null ? EngineTelemetryTransport.InProcess : EngineTelemetryTransport.Http,
                role,
                eligible: false);
            request.SetOutcome(EngineTelemetryOutcome.Unknown);
            ExecutionScope scope = new(context, request, httpContext);
            context.ContextData[CONTEXT_KEY] = scope;
            return scope;
        }

        public override IDisposable ExecuteOperation(GraphQLRequestContext context)
        {
            // The parsed document is now available, including on the operation-cache path.
            // Mark before resolving fields; BeginRequest already captured the correct epoch.
            if (TryGetScope(context, out ExecutionScope? scope))
            {
                scope.MarkEligible();
            }

            return EmptyScope;
        }

        public override void RequestError(GraphQLRequestContext context, Exception exception)
        {
            if (TryGetScope(context, out ExecutionScope? scope))
            {
                scope.Request.SetOutcome(exception is OperationCanceledException
                    ? EngineTelemetryOutcome.Canceled
                    : EngineTelemetryOutcome.Failure);
            }
        }

        public override void RequestError(GraphQLRequestContext context, IError error)
        {
            if (TryGetScope(context, out ExecutionScope? scope))
            {
                scope.Request.SetOutcome(error.Exception is OperationCanceledException
                    ? EngineTelemetryOutcome.Canceled
                    : EngineTelemetryOutcome.Failure);
            }
        }

        public override void ValidationErrors(GraphQLRequestContext context, IReadOnlyList<IError> errors)
        {
            if (errors.Count > 0 && TryGetScope(context, out ExecutionScope? scope))
            {
                scope.Request.SetOutcome(EngineTelemetryOutcome.Failure);
            }
        }

        internal static EngineTelemetryOutcome ClassifyResult(
            IExecutionResult? result, EngineTelemetryOutcome diagnosticOutcome, bool canceled)
        {
            if (canceled || diagnosticOutcome == EngineTelemetryOutcome.Canceled)
            {
                return EngineTelemetryOutcome.Canceled;
            }

            // HC 16 exposes OperationResult (not the older IOperationResult interface).
            // Inspect only error presence and the typed null-data flag, never serialize/read
            // JSON, error text, response values or extensions for product telemetry.
            if (result is OperationResult operationResult)
            {
                if (operationResult.Errors.Count > 0)
                {
                    return operationResult.Data is { IsValueNull: false }
                        ? EngineTelemetryOutcome.PartialFailure
                        : EngineTelemetryOutcome.Failure;
                }

                if (diagnosticOutcome is EngineTelemetryOutcome.Failure or EngineTelemetryOutcome.PartialFailure)
                {
                    return diagnosticOutcome;
                }

                return operationResult.HasNext == true || operationResult.Data is null
                    ? EngineTelemetryOutcome.Unknown
                    : EngineTelemetryOutcome.Success;
            }

            // A response stream has not completed when ExecuteRequest returns. Do not claim
            // success from its HTTP status. DAB's ordinary queries/mutations use OperationResult.
            return diagnosticOutcome;
        }

        internal static bool IsDataOperation(DocumentNode? document, string? operationName)
        {
            if (document is null)
            {
                return false;
            }

            operationName = string.IsNullOrEmpty(operationName) ? null : operationName;
            OperationDefinitionNode? operation = null;
            Dictionary<string, FragmentDefinitionNode> fragments = new(StringComparer.Ordinal);
            foreach (IDefinitionNode definition in document.Definitions)
            {
                if (definition is FragmentDefinitionNode fragment)
                {
                    fragments.TryAdd(fragment.Name.Value, fragment);
                }
                else if (definition is OperationDefinitionNode candidate &&
                         (operationName is null || string.Equals(candidate.Name?.Value, operationName, StringComparison.Ordinal)))
                {
                    if (operation is not null)
                    {
                        // No unique selected operation means there was no data execution.
                        return false;
                    }

                    operation = candidate;
                }
            }

            if (operation is null)
            {
                return false;
            }

            // Expand root fragments iteratively, with cycle protection even for invalid
            // documents. Aliases and an operation called "IntrospectionQuery" do not decide
            // eligibility. Nested fields below a data field are not separate requests.
            Stack<SelectionSetNode> pending = new();
            HashSet<string> visitedFragments = new(StringComparer.Ordinal);
            pending.Push(operation.SelectionSet);
            while (pending.TryPop(out SelectionSetNode? selections))
            {
                foreach (ISelectionNode selection in selections.Selections)
                {
                    switch (selection)
                    {
                        case FieldNode field when field.Name.Value is not ("__schema" or "__type" or "__typename"):
                            return true;
                        case InlineFragmentNode inlineFragment:
                            pending.Push(inlineFragment.SelectionSet);
                            break;
                        case FragmentSpreadNode spread when visitedFragments.Add(spread.Name.Value) &&
                            fragments.TryGetValue(spread.Name.Value, out FragmentDefinitionNode? fragment):
                            pending.Push(fragment.SelectionSet);
                            break;
                    }
                }
            }

            return false;
        }

        private static bool TryGetScope(
            GraphQLRequestContext context,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ExecutionScope? scope)
        {
            context.ContextData.TryGetValue(CONTEXT_KEY, out object? state);
            scope = state as ExecutionScope;
            return scope is not null;
        }

        private static HttpContext? GetHttpContext(GraphQLRequestContext context)
        {
            if (context.ContextData.TryGetValue(nameof(HttpContext), out object? value) && value is HttpContext httpContext)
            {
                return httpContext;
            }

            return context.RequestServices.GetService<IHttpContextAccessor>()?.HttpContext;
        }

        private sealed class ExecutionScope : IDisposable
        {
            private readonly GraphQLRequestContext _context;
            private readonly HttpContext? _httpContext;
            private bool _eligible;
            private int _disposed;

            internal ExecutionScope(GraphQLRequestContext context, EngineTelemetryRequestScope request, HttpContext? httpContext)
            {
                _context = context;
                Request = request;
                _httpContext = httpContext;
            }

            internal EngineTelemetryRequestScope Request { get; }

            internal void MarkEligible()
            {
                // Parse failures may have no document info. Only the parsed AST can establish
                // eligibility; the source text or operation name alone cannot do so.
                if (!_eligible && IsDataOperation(_context.OperationDocumentInfo?.Document, _context.Request.OperationName))
                {
                    _eligible = true;
                    Request.MarkEligible();
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                try
                {
                    // Validation failures can finish without invoking ExecuteOperation.
                    MarkEligible();
                    if (_eligible)
                    {
                        if (_context.Result is OperationResultBatch batch)
                        {
                            // Variable batching has one RequestContext, but each result is a
                            // separate execution. Fork the captured start/configuration rather
                            // than starting a request in the possibly reloaded current epoch.
                            // The original scope restores ambient state only; do not count it.
                            foreach (IExecutionResult result in batch.Results)
                            {
                                CompleteResult(Request.ForkForCompletion(), result);
                            }
                        }
                        else
                        {
                            CompleteResult(Request, _context.Result);
                        }
                    }
                }
                finally
                {
                    _context.ContextData.Remove(CONTEXT_KEY);
                    Request.Dispose();
                }
            }

            private void CompleteResult(EngineTelemetryRequestScope request, IExecutionResult? result)
            {
                CancellationToken aborted = _context.RequestAborted;
                EngineTelemetryOutcome outcome = ClassifyResult(result, Request.Outcome, aborted.IsCancellationRequested);
                request.SetOutcome(outcome);
                if (_httpContext is not null)
                {
                    // Capture only the scope and closed outcome: neither the pooled context nor
                    // the result (including a batch member's buffers) may survive in callbacks.
                    _ = new EngineTelemetryHttpCompletion(
                        _httpContext, request.Complete, () => outcome, inferSuccessFromHttp: false);
                }
                else if (result is IResponseStream stream)
                {
                    // A stream is still running, including one in a variable batch. Its owner
                    // must dispose it after consumption. Without payload-level observation,
                    // cleanup preserves Unknown rather than manufacturing success.
                    stream.RegisterForCleanup(() => RecordCompletion(request,
                        aborted.IsCancellationRequested ? EngineTelemetryOutcome.Canceled : outcome));
                }
                else
                {
                    RecordCompletion(request, outcome);
                }
            }

            private static void RecordCompletion(EngineTelemetryRequestScope request, EngineTelemetryOutcome outcome)
            {
                try
                {
                    request.Complete(outcome);
                }
                catch (Exception)
                {
                    // Product collection must not turn an embedded result into an execution error.
                }
            }
        }
    }
}

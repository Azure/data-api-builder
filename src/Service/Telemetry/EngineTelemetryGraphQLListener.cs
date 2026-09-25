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
using HotChocolate.Execution.Processing;
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
            // Compiled selections and coerced variables are available, even on operation-cache
            // hits. Establish eligibility before resolvers, independently for each variable set.
            if (TryGetScope(context, out ExecutionScope? scope))
            {
                scope.CaptureEligibility();
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

        internal static bool HasDataIntent(DocumentNode? document, string? operationName)
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

            // Failure-only fallback for requests rejected before effective selection exists.
            // This identifies attempted data, never proves execution or successful usage.
            // Expand root fragments with cycle protection even for invalid documents.
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
            private bool[]? _effectiveEligibility;
            private int _disposed;

            internal ExecutionScope(GraphQLRequestContext context, EngineTelemetryRequestScope request, HttpContext? httpContext)
            {
                _context = context;
                Request = request;
                _httpContext = httpContext;
            }

            internal EngineTelemetryRequestScope Request { get; }

            internal void CaptureEligibility()
            {
                if (_effectiveEligibility is not null)
                {
                    return;
                }

                try
                {
                    if (!_context.TryGetOperation(out var operation) || _context.VariableValues.IsDefaultOrEmpty)
                    {
                        return;
                    }

                    bool[] eligibility = new bool[_context.VariableValues.Length];
                    bool anyData = false;
                    for (int index = 0; index < eligibility.Length; index++)
                    {
                        ulong includeFlags = operation.CreateIncludeFlags(_context.VariableValues[index]);
                        foreach (Selection selection in operation.RootSelectionSet.Selections)
                        {
                            if (!selection.Field.IsIntrospectionField && selection.IsIncluded(includeFlags))
                            {
                                eligibility[index] = true;
                                anyData = true;
                                break;
                            }
                        }
                    }

                    // Retain only closed decisions, not variable values, names, or compiled
                    // operations. Never attach request-specific flags to HC's cached operation.
                    _effectiveEligibility = eligibility;
                    if (anyData)
                    {
                        Request.MarkEligible();
                    }
                }
                catch (Exception)
                {
                    // Optional classification cannot change GraphQL execution. Unavailable
                    // effective selections cannot establish a successful request milestone.
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
                    // Failures or middleware short circuits can skip ExecuteOperation.
                    CaptureEligibility();
                    if (_context.Result is OperationResultBatch batch)
                    {
                        for (int position = 0; position < batch.Results.Count; position++)
                        {
                            IExecutionResult result = batch.Results[position];
                            // HC indexes ordinary variable results. Deferred stream results
                            // have no index, but the pinned SDK preserves variable-set order.
                            int? index = result is OperationResult operationResult ? operationResult.VariableIndex
                                : batch.Results.Count == _effectiveEligibility?.Length ? position : null;
                            if (index is int variableIndex && IsEffectiveData(variableIndex))
                            {
                                // Result-local errors decide each member's outcome. A shared
                                // diagnostic failure must not poison error-free batch peers.
                                CompleteResult(Request.ForkForCompletion(), result, EngineTelemetryOutcome.Unknown);
                            }
                        }
                    }
                    else
                    {
                        EngineTelemetryOutcome outcome = ClassifyResult(_context.Result, Request.Outcome, _context.RequestAborted.IsCancellationRequested);
                        bool eligible = _effectiveEligibility is { Length: 1 }
                            ? _effectiveEligibility[0]
                            : IsFailedDataRequest(outcome);
                        if (eligible)
                        {
                            Request.MarkEligible();
                            CompleteResult(Request, _context.Result, Request.Outcome);
                        }
                    }
                }
                finally
                {
                    _context.ContextData.Remove(CONTEXT_KEY);
                    Request.Dispose();
                }
            }

            private bool IsEffectiveData(int variableIndex) => _effectiveEligibility is not null &&
                variableIndex >= 0 && variableIndex < _effectiveEligibility.Length && _effectiveEligibility[variableIndex];

            private bool IsFailedDataRequest(EngineTelemetryOutcome outcome)
            {
                if (outcome is not (EngineTelemetryOutcome.Failure or EngineTelemetryOutcome.PartialFailure or EngineTelemetryOutcome.Canceled))
                {
                    return false;
                }

                if (_effectiveEligibility is not null)
                {
                    // A whole batch can fail before producing individual results. Count one
                    // observed failed request only if at least one effective member was data.
                    return Array.Exists(_effectiveEligibility, eligible => eligible);
                }

                return HasDataIntent(_context.OperationDocumentInfo?.Document, _context.Request.OperationName);
            }

            private void CompleteResult(EngineTelemetryRequestScope request, IExecutionResult? result, EngineTelemetryOutcome diagnosticOutcome)
            {
                CancellationToken aborted = _context.RequestAborted;
                EngineTelemetryOutcome outcome = ClassifyResult(result, diagnosticOutcome, aborted.IsCancellationRequested);
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

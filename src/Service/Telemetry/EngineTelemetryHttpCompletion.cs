// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// The HTTP response owns this small completion state, not an event queue. Cancellation,
    /// a failed pipeline and OnCompleted compete for one terminal observation. No execution
    /// context is retained by the cancellation registration and no result body is inspected.
    /// </summary>
    internal sealed class EngineTelemetryHttpCompletion
    {
        private readonly HttpContext _context;
        private readonly Action<EngineTelemetryOutcome, int?> _complete;
        private readonly Func<EngineTelemetryOutcome> _outcome;
        private readonly bool _inferSuccessFromHttp;
        private CancellationTokenRegistration _abortRegistration;
        private int _completed;

        internal EngineTelemetryHttpCompletion(
            HttpContext context,
            Action<EngineTelemetryOutcome, int?> complete,
            Func<EngineTelemetryOutcome> outcome,
            bool inferSuccessFromHttp)
        {
            _context = context;
            _complete = complete;
            _outcome = outcome;
            _inferSuccessFromHttp = inferSuccessFromHttp;
            // Batch executions can finish concurrently. ASP.NET's response callback collection
            // is not a concurrent collection; serialize our registrations on this response.
            lock (context)
            {
                context.Response.OnCompleted(static state => ((EngineTelemetryHttpCompletion)state).ResponseCompletedAsync(), this);
            }

            _abortRegistration = context.RequestAborted.UnsafeRegister(
                static state => ((EngineTelemetryHttpCompletion)state!).Fail(EngineTelemetryOutcome.Canceled), this);

            // UnsafeRegister can invoke synchronously for an already canceled token.
            if (Volatile.Read(ref _completed) != 0)
            {
                _abortRegistration.Unregister();
            }
        }

        internal void Fail(EngineTelemetryOutcome outcome) => Complete(outcome, httpStatusCode: null);

        internal static EngineTelemetryOutcome ClassifyOutcome(
            EngineTelemetryOutcome diagnosticOutcome, int statusCode, bool inferSuccessFromHttp)
        {
            if (diagnosticOutcome is EngineTelemetryOutcome.Canceled or EngineTelemetryOutcome.Failure or EngineTelemetryOutcome.PartialFailure)
            {
                return diagnosticOutcome;
            }

            if (statusCode >= StatusCodes.Status400BadRequest)
            {
                return EngineTelemetryOutcome.Failure;
            }

            if (statusCode >= StatusCodes.Status200OK && statusCode < StatusCodes.Status300MultipleChoices)
            {
                return inferSuccessFromHttp ? EngineTelemetryOutcome.Success : diagnosticOutcome;
            }

            return EngineTelemetryOutcome.Unknown;
        }

        private Task ResponseCompletedAsync()
        {
            if (Volatile.Read(ref _completed) != 0)
            {
                return Task.CompletedTask;
            }

            if (_context.RequestAborted.IsCancellationRequested)
            {
                Fail(EngineTelemetryOutcome.Canceled);
            }
            else
            {
                int statusCode = _context.Response.StatusCode;
                Complete(ClassifyOutcome(_outcome(), statusCode, _inferSuccessFromHttp), statusCode);
            }

            return Task.CompletedTask;
        }

        private void Complete(EngineTelemetryOutcome outcome, int? httpStatusCode)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                _abortRegistration.Unregister();
                try
                {
                    _complete(outcome, httpStatusCode);
                }
                catch (Exception)
                {
                    // Product telemetry cannot fail the response or enter customer diagnostics.
                }
            }
        }
    }
}

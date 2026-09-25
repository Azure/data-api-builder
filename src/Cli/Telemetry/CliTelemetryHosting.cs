// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Telemetry;

namespace Cli.Telemetry
{
    /// <summary>
    /// Stateless adapters for an explicitly owned CLI session. Only Main bootstraps collection;
    /// direct Execute callers never consult the environment or acquire a product sender.
    /// </summary>
    internal static class CliTelemetryHosting
    {
        internal static CliTelemetrySession CreateStandalone()
        {
            try
            {
                if (ProductTelemetryBootstrap.TryGetDestination(out ApplicationInsightsTelemetryDestination? destination))
                {
                    return CliTelemetrySession.Create(() => ProductTelemetrySenderPool.Acquire(destination),
                        enableSyntheticCollection: true);
                }
            }
            catch (Exception)
            {
                // Optional telemetry must not prevent a command from executing.
            }

            return CliTelemetrySession.Create();
        }

        internal static void ObserveConfiguration(CliTelemetrySession? telemetry, IFileSystem fileSystem, string path)
        {
            if (telemetry is { IsEnabled: true })
            {
                telemetry.ObserveConfiguration(ResolveConfigurationPath(fileSystem, path));
            }
        }

        /// <summary>
        /// Resolve only the path the command already selected, using that command's filesystem.
        /// The result is local identity-store input, never an event property or launch-context field.
        /// An unresolvable target remains unknown rather than being guessed from the process CWD.
        /// </summary>
        internal static string? ResolveConfigurationPath(IFileSystem fileSystem, string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? null : fileSystem.Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Classify by exception type only; never retain an exception or its diagnostic text.</summary>
        internal static void MarkException(CliTelemetrySession? telemetry, Exception exception,
            CliTelemetryFailureCategory category = CliTelemetryFailureCategory.Execution)
        {
            if (telemetry is not { IsEnabled: true })
            {
                return;
            }

            // Existing synchronous Task.Wait/Result call sites can wrap a single cancellation
            // or I/O failure. Inspect the type without changing the exception the caller sees.
            while (exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            {
                exception = aggregate.InnerExceptions[0];
            }

            if (exception is OperationCanceledException)
            {
                telemetry.MarkFailure(CliTelemetryOutcome.Canceled, CliTelemetryFailureCategory.Canceled);
            }
            else if (category == CliTelemetryFailureCategory.Execution
                && exception is DataApiBuilderException { SubStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError })
            {
                telemetry.MarkFailure(CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Configuration);
            }
            else
            {
                telemetry.MarkFailure(CliTelemetryOutcome.ExecutionFailure,
                    exception is IOException or UnauthorizedAccessException ? CliTelemetryFailureCategory.Storage : category);
            }
        }
    }
}

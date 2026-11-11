// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Exceptions;
using Cli.Constants;

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

        /// <summary>
        /// Observe the selected root and the model from the command's one ordinary load.
        /// Keep diagnostics, exception handling, capture scope and replacement policy with the
        /// caller/loader; never discover another config, reread it or substitute a cached model.
        /// </summary>
        internal static bool TryLoadConfiguration(FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem,
            string path, CliTelemetrySession? telemetry, [NotNullWhen(true)] out RuntimeConfig? config,
            DeserializationVariableReplacementSettings? replacementSettings = null)
        {
            ObserveConfiguration(telemetry, fileSystem, path);
            if (!loader.TryLoadConfig(path, out config, replacementSettings: replacementSettings))
            {
                telemetry?.MarkFailure(CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Configuration);
                return false;
            }

            ObserveConfigurationDetails(telemetry, fileSystem, path, config);
            return true;
        }

        internal static void ObserveConfiguration(CliTelemetrySession? telemetry, IFileSystem fileSystem, string path)
        {
            if (telemetry is { IsEnabled: true })
            {
                telemetry.ObserveConfiguration(ResolveConfigurationPath(fileSystem, path));
            }
        }

        /// <summary>
        /// Project only the model the command already loaded or successfully wrote. This does
        /// no configuration discovery, identity I/O, or model serialization and retains no model.
        /// A successful save can supply the bytes already written, never a telemetry-only reread.
        /// </summary>
        internal static void ObserveConfigurationDetails(CliTelemetrySession? telemetry, IFileSystem fileSystem,
            string path, RuntimeConfig config, string? writtenJson = null)
        {
            if (telemetry is not { CanObserveCommand: true })
            {
                return;
            }

            string? root = ResolveConfigurationPath(fileSystem, path);
            try
            {
                if (writtenJson is not null)
                {
                    // 'with' updates can retain stale original-input provenance. Use the actual
                    // successful write's structural presence, without retaining or loading JSON.
                    config = config with
                    {
                        TelemetryPresence = TelemetryConfigurationPresence.TryCaptureBounded(writtenJson, config, enabled: true)
                    };
                }

                telemetry.ObserveConfigurationDetails(root, config, saved: writtenJson is not null);
            }
            catch (Exception)
            {
                // A written config remains a successful write even if optional evidence is lost.
                telemetry.ObserveConfigurationDetails(root, null);
            }
        }

        internal static IDisposable? BeginConfigurationCapture(CliTelemetrySession? telemetry)
            => TelemetryConfigurationPresence.BeginCommandCapture(telemetry is { CanObserveCommand: true }
                ? telemetry.IsConfigurationCaptureEnabled : null);

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

        /// <summary>Classify completion without changing the command's legacy return code.</summary>
        internal static CliTelemetryCommandResult ClassifyReturnCode(int returnCode, CliTelemetrySession? telemetry = null,
            CliTelemetryCommandResult? terminalResult = null, bool isStartCommand = false)
        {
            if (terminalResult is CliTelemetryCommandResult terminal)
            {
                // A terminal export result distinguishes no schema from a successful retry;
                // neither inherits an earlier recoverable attempt's failure category.
                return terminal;
            }

            if (isStartCommand && telemetry?.HasEngineStartupFailed == true)
            {
                // Failed initialization can stop the host normally with a legacy zero exit.
                return new(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Initialization);
            }

            if (returnCode == CliReturnCode.SUCCESS)
            {
                return new(CliTelemetryOutcome.Success, CliTelemetryFailureCategory.None);
            }

            // A bool/exit code cannot recover the cause of a newly added false branch. Without
            // a typed first-failure hint, Unknown is honest, not success or an invented cause.
            // Do not use default(CliTelemetryCommandResult): the enum's default is Success.
            return telemetry is { HasFailure: true }
                ? new(telemetry.FailureOutcome, telemetry.FailureCategory)
                : new(CliTelemetryOutcome.Unknown, CliTelemetryFailureCategory.Unknown);
        }

        /// <summary>Classify by exception type only; never retain an exception or its diagnostic text.</summary>
        internal static CliTelemetryCommandResult ClassifyException(Exception exception,
            CliTelemetryFailureCategory category = CliTelemetryFailureCategory.Execution)
        {
            // Existing synchronous Task.Wait/Result call sites can wrap a single cancellation
            // or I/O failure. Inspect the type without changing the exception the caller sees.
            while (exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            {
                exception = aggregate.InnerExceptions[0];
            }

            return exception switch
            {
                OperationCanceledException => new(CliTelemetryOutcome.Canceled, CliTelemetryFailureCategory.Canceled),
                DataApiBuilderException { SubStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError }
                    when category == CliTelemetryFailureCategory.Execution
                    => new(CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Configuration),
                IOException or UnauthorizedAccessException
                    => new(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Storage),
                _ => new(CliTelemetryOutcome.ExecutionFailure, category)
            };
        }

        internal static void MarkException(CliTelemetrySession? telemetry, Exception exception,
            CliTelemetryFailureCategory category = CliTelemetryFailureCategory.Execution)
        {
            if (telemetry is { IsEnabled: true })
            {
                CliTelemetryCommandResult failure = ClassifyException(exception, category);
                telemetry.MarkFailure(failure.Outcome, failure.FailureCategory);
            }
        }
    }
}

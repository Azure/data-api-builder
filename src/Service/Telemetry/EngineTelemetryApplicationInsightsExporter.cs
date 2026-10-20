// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Pipeline;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Uses the supported Azure Monitor custom-event exporter on the delivery worker, never a
    /// request thread. DAB owns the only queue and retry loop; SDK offline storage is disabled.
    /// </summary>
    /// <remarks>
    /// Validation only: 1.9.0 shares SDK transmitters by connection string and caches some process
    /// settings. Use a dedicated process with SDK statistics disabled before startup. Distinct
    /// adapter objects do not guarantee isolation from a foreign exporter with the same routing.
    /// Product owners share this adapter through ProductTelemetrySenderPool instead.
    /// Production and public embedded enablement remain off pending that integration review.
    /// </remarks>
    internal sealed class EngineTelemetryApplicationInsightsExporter : IProductTelemetryExporter<IProductTelemetryEvent>, IEngineTelemetryExporter
    {
        internal const string STATSBEAT_DISABLED_VARIABLE = "APPLICATIONINSIGHTS_STATSBEAT_DISABLED";
        internal const string SDK_STATS_DISABLED_VARIABLE = "APPLICATIONINSIGHTS_SDKSTATS_DISABLED";
        private static readonly TimeSpan _exportTimeout = TimeSpan.FromSeconds(1);
        private readonly AsyncLocal<EngineTelemetryExportAttempt?> _attempt;
        private readonly SynchronousExportProcessor _processor;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly HttpClientTransport? _transport;
        private int _disposed;

        internal EngineTelemetryApplicationInsightsExporter(ApplicationInsightsTelemetryDestination destination)
            : this(CreateSdkComponents(destination, null))
        {
        }

        /// <summary>Test-only HTTP injection. Both SDK statistics opt-outs must still be set.</summary>
        internal EngineTelemetryApplicationInsightsExporter(ApplicationInsightsTelemetryDestination destination, HttpMessageHandler handler)
            : this(CreateSdkComponents(destination, handler ?? throw new ArgumentNullException(nameof(handler))))
        {
        }

        /// <summary>Unit-test seam with no Azure SDK initialization or network resources.</summary>
        internal EngineTelemetryApplicationInsightsExporter(BaseExporter<LogRecord> exporter)
            : this(new Components(exporter ?? throw new ArgumentNullException(nameof(exporter)), new(), null))
        {
        }

        private EngineTelemetryApplicationInsightsExporter(Components components)
        {
            _attempt = components.Attempt;
            _transport = components.Transport;
            _processor = new(components.Exporter, _attempt);
            ILoggerFactory? factory = null;
            try
            {
                // This private factory never registers with the host or reads its configuration.
                factory = LoggerFactory.Create(builder =>
                {
                    builder.ClearProviders();
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddOpenTelemetry(options =>
                    {
                        options.IncludeScopes = false;
                        options.IncludeFormattedMessage = false;
                        options.ParseStateValues = true;
                        options.SetResourceBuilder(ResourceBuilder.CreateEmpty());
                        options.AddProcessor(_processor);
                    });
                });
                _logger = factory.CreateLogger(string.Empty);
                _loggerFactory = factory;
            }
            catch
            {
                try
                {
                    factory?.Dispose();
                }
                finally
                {
                    _processor.Dispose();
                    _transport?.Dispose();
                }

                throw new InvalidOperationException("Product telemetry logger initialization failed.");
            }
        }

        // The SDK exposes synchronous Export. Do not add Task.Run or another queue per event:
        // the existing delivery worker owns this call and its bounded cancellation/deadline.
        public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            => ExportAsync((IProductTelemetryEvent)record, cancellationToken);

        public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            => ValueTask.FromResult(Export(record, cancellationToken));

        private bool Export(IProductTelemetryEvent record, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (Volatile.Read(ref _disposed) != 0 || cancellationToken.IsCancellationRequested || !record.IsSynthetic)
            {
                return false;
            }

            EngineTelemetryExportAttempt? previous = _attempt.Value;
            try
            {
                using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(_exportTimeout);
                EngineTelemetryExportAttempt current = new(record, budget.Token);
                _attempt.Value = current;
                budget.Token.ThrowIfCancellationRequested();
                // Suppress inside OnEnd, not here: suppressing Log would discard the event.
                _logger.Log(LogLevel.Information, default, ApplicationInsightsEventAttributes.Create(record),
                    null, static (_, _) => string.Empty);
                return current.Result == ExportResult.Success && !budget.IsCancellationRequested;
            }
            catch (Exception)
            {
                // Optional telemetry never forwards SDK exceptions/payloads to customer logging.
                return false;
            }
            finally
            {
                _attempt.Value = previous;
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
                _loggerFactory.Dispose();
            }
            finally
            {
                _processor.Dispose();
                _transport?.Dispose();
            }
        }

        internal static bool AreSdkStatisticsDisabled() => AreSdkStatisticsDisabled(Environment.GetEnvironmentVariable);

        internal static bool AreSdkStatisticsDisabled(Func<string, string?> readEnvironmentVariable) =>
            string.Equals(readEnvironmentVariable(STATSBEAT_DISABLED_VARIABLE), "true", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(readEnvironmentVariable(SDK_STATS_DISABLED_VARIABLE), "true", StringComparison.OrdinalIgnoreCase);

        internal static AzureMonitorExporterOptions CreateExporterOptions(string connectionString, HttpClientTransport transport)
        {
            AzureMonitorExporterOptions options = new()
            {
                ConnectionString = connectionString,
                Transport = transport,
                DisableOfflineStorage = true,
                EnableLiveMetrics = false,
                EnableTraceBasedLogsSampler = false,
                EnableStandardMetrics = false,
                EnablePerformanceCounters = false
            };
            options.Retry.MaxRetries = 0;
            options.Retry.NetworkTimeout = _exportTimeout;
            options.Diagnostics.IsLoggingEnabled = false;
            options.Diagnostics.IsLoggingContentEnabled = false;
            options.Diagnostics.IsDistributedTracingEnabled = false;
            options.Diagnostics.IsTelemetryEnabled = false;
            options.Diagnostics.ApplicationId = null;
            options.Diagnostics.LoggedContentSizeLimit = 0;
            options.Diagnostics.LoggedHeaderNames.Clear();
            options.Diagnostics.LoggedQueryParameters.Clear();
            return options;
        }

        private static Components CreateSdkComponents(ApplicationInsightsTelemetryDestination destination, HttpMessageHandler? handler)
        {
            ArgumentNullException.ThrowIfNull(destination);
            // Temporary validation prerequisite, not a custom-event requirement of Azure Monitor.
            // Never mutate process-wide environment/AppContext settings from product code.
            if (!AreSdkStatisticsDisabled())
            {
                throw new InvalidOperationException("Synthetic product telemetry requires SDK statistics disabled before process startup.");
            }

            AsyncLocal<EngineTelemetryExportAttempt?> attempt = new();
            HttpClient client = new(new EngineTelemetrySdkTransportHandler(handler ?? CreateHttpHandler(), attempt, destination.TrackEndpoint))
            {
                Timeout = _exportTimeout
            };
            HttpClientTransport transport = new(client);
            try
            {
                return new(new AzureMonitorLogExporter(CreateExporterOptions(destination.ConnectionString, transport)), attempt, transport);
            }
            catch
            {
                transport.Dispose();
                throw new InvalidOperationException("Product telemetry SDK initialization failed.");
            }
        }

        internal static SocketsHttpHandler CreateHttpHandler() => new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ActivityHeadersPropagator = null,
            ConnectTimeout = _exportTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        private sealed record Components(
            BaseExporter<LogRecord> Exporter, AsyncLocal<EngineTelemetryExportAttempt?> Attempt, HttpClientTransport? Transport);

        private sealed class SynchronousExportProcessor(
            BaseExporter<LogRecord> exporter, AsyncLocal<EngineTelemetryExportAttempt?> attempt) : BaseProcessor<LogRecord>
        {
            private int _disposed;

            public override void OnEnd(LogRecord data)
            {
                EngineTelemetryExportAttempt? current = attempt.Value;
                if (current is null || current.Token.IsCancellationRequested)
                {
                    return;
                }

                data.Timestamp = current.Record.OccurredAt.UtcDateTime;
                data.ObservedTimestamp = data.Timestamp;
                data.TraceId = default;
                data.SpanId = default;
                data.TraceFlags = default;
                data.TraceState = null;
                data.Body = null;
                data.FormattedMessage = null;
                data.CategoryName = null;
                data.EventId = default;
                data.Exception = null;
                using IDisposable suppression = SuppressInstrumentationScope.Begin();
                Activity? previous = Activity.Current;
                try
                {
                    Activity.Current = null;
                    current.Token.ThrowIfCancellationRequested();
                    using Batch<LogRecord> batch = new(data);
                    // BaseExportProcessor propagates a parent resource to the SDK, which can
                    // infer a hostname even from an empty resource. Direct public Export avoids
                    // that enrichment and owns no SDK batch queue or customer provider.
                    current.Result = exporter.Export(in batch);
                }
                catch (Exception)
                {
                    current.Result = ExportResult.Failure;
                }
                finally
                {
                    Activity.Current = previous;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    exporter.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}

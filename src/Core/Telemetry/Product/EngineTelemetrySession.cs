// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using static Azure.DataApiBuilder.Core.Telemetry.Product.EngineTelemetryValueFormatter;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// One engine's product telemetry, isolated from all customer diagnostic providers. Only
    /// deliberate synthetic validation can enable this implementation before privacy approval.
    /// Request adapters supply closed categories and never forward diagnostics or payloads.
    /// </summary>
    internal sealed class EngineTelemetrySession : IProductTelemetryControl, IDisposable
    {
        private readonly object _sync = new();
        private readonly TimeProvider _clock;
        private readonly EngineTelemetryAggregator _aggregator;
        private readonly AsyncLocal<EngineTelemetryRequestScope?> _request = new();
        private readonly AsyncLocal<int> _operationDepth = new();
        private readonly EngineTelemetryDelivery? _delivery;
        private readonly ImmutableDictionary<string, string> _context;
        private readonly long _started;
        private readonly string? _configPath;
        private readonly Func<string?, EngineTelemetryIdentity> _resolveIdentity;
        private ITimer? _timer;
        private Guid _sessionId;
        private long _sequence;
        private long _readyAt;
        private long _lastHeartbeat;
        private bool _enabled;
        private bool _hostReady;
        private bool _ready;
        private bool _firstServed;
        private bool _firstSuccess;
        private bool _startupFailed;
        private RuntimeConfig? _config;
        private EngineTelemetryConfiguration _configuration;
        private long _configurationAcceptanceGeneration;
        private ImmutableDictionary<string, string> _snapshot = ImmutableDictionary<string, string>.Empty;
        private EngineTelemetryIdentity? _identity;
        private Lazy<EngineTelemetryIdentity>? _identityResolution;
        private Task? _stopTask;

        private EngineTelemetrySession(bool enabled, Func<IEngineTelemetryExporter>? exporterFactory,
            string? configPath, string executionMode, TimeProvider clock, Action? showNotice,
            Func<string?, EngineTelemetryIdentity>? resolveIdentity, bool startTimer, Func<string, string?> readEnvironmentVariable)
        {
            _clock = clock;
            _configPath = configPath;
            _resolveIdentity = resolveIdentity ?? EngineTelemetryIdentityStore.Resolve;
            _aggregator = EngineTelemetryAggregator.Create(new() { EnableSyntheticCollection = enabled }, clock, _ => null);
            _context = ImmutableDictionary<string, string>.Empty;
            if (!enabled || exporterFactory is null)
            {
                return;
            }

            try
            {
                // No notice/sender/identity work occurs on the disabled path. Failure to show
                // the required notice fails closed, without using the customer's logging sinks.
                (showNotice ?? ShowNotice)();
                _sessionId = Guid.NewGuid();
                HealthProbeToken = Guid.NewGuid().ToString("N");
                _started = clock.GetTimestamp();
                _lastHeartbeat = _started;
                _context = EngineTelemetryContext.Create(executionMode, readEnvironmentVariable);
                _delivery = new EngineTelemetryDelivery(exporterFactory);
                _enabled = true;
                Emit("dab.engine.process_started", 0, ImmutableDictionary<string, string>.Empty);
                if (startTimer)
                {
                    if (ExecutionContext.IsFlowSuppressed())
                    {
                        _timer = clock.CreateTimer(_ => Tick(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                    }
                    else
                    {
                        using (ExecutionContext.SuppressFlow())
                        {
                            _timer = clock.CreateTimer(_ => Tick(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                        }
                    }
                }
            }
            catch (Exception)
            {
                _enabled = false;
                _aggregator.Disable();
                _delivery?.Disable();
            }
        }

        internal static EngineTelemetrySession Create(Func<IEngineTelemetryExporter>? exporterFactory = null,
            bool enableSyntheticCollection = false, string? configPath = null, string executionMode = "embedded",
            TimeProvider? clock = null, Func<string, string?>? readEnvironmentVariable = null,
            Action? showNotice = null, Func<string?, EngineTelemetryIdentity>? resolveIdentity = null, bool startTimer = true)
        {
            bool enabled = enableSyntheticCollection && exporterFactory is not null;
            if (enabled)
            {
                enabled = !ProductTelemetryPolicy.IsOptedOut((readEnvironmentVariable ?? Environment.GetEnvironmentVariable)(ProductTelemetryPolicy.OPT_OUT_ENV_VAR));
            }

            return new(enabled, exporterFactory, configPath, executionMode, clock ?? TimeProvider.System, showNotice, resolveIdentity, startTimer,
                readEnvironmentVariable ?? Environment.GetEnvironmentVariable);
        }

        public bool IsEnabled => Volatile.Read(ref _enabled);

        // In-memory self-probe correlation only. Never exported, persisted or used for auth.
        internal string? HealthProbeToken { get; }

        public bool IsReady
        {
            get
            {
                lock (_sync)
                {
                    return _enabled && _ready;
                }
            }
        }

        public EngineTelemetryRequestScope? CurrentRequest => _request.Value;

        public void AcceptConfiguration(RuntimeConfig config, string delivery = "startup", string? configPath = null,
            bool onlyIfUnconfigured = false)
        {
            if (!IsEnabled)
            {
                return;
            }

            try
            {
                Lazy<EngineTelemetryIdentity> identityResolution;
                long acceptanceGeneration;
                lock (_sync)
                {
                    if (!_enabled || (onlyIfUnconfigured && _configurationAcceptanceGeneration != 0))
                    {
                        return;
                    }

                    // Reserve acceptance order before projection or I/O. An older waiter must
                    // not overwrite a newer accepted model merely because it finishes later.
                    acceptanceGeneration = checked(++_configurationAcceptanceGeneration);
                    if (ReferenceEquals(config, _config))
                    {
                        return;
                    }

                    // Resolve once, but never hold the session gate across filesystem I/O.
                    // Disable/shutdown must not wait for an identity read on a slow volume.
                    identityResolution = _identityResolution ??= new(
                        () => _resolveIdentity(configPath ?? _configPath), LazyThreadSafetyMode.ExecutionAndPublication);
                }

                ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config)
                    .Add("configuration_delivery", delivery is "startup" or "late_configuration" or "hot_reload" ? delivery : "unknown");
                EngineTelemetryIdentity identity = identityResolution.Value;
                lock (_sync)
                {
                    // An in-flight read can finish after stop. It cannot revive collection or
                    // emit a stale configuration after a newer acceptance superseded it.
                    if (!_enabled || acceptanceGeneration != _configurationAcceptanceGeneration || ReferenceEquals(config, _config))
                    {
                        return;
                    }

                    _identity ??= identity;
                    _configuration = _aggregator.AcceptConfiguration();
                    _config = config;
                    _snapshot = snapshot;
                    if (_ready)
                    {
                        Emit("dab.engine.configuration_changed", _configuration.Epoch, snapshot);
                    }
                    else
                    {
                        TryReady();
                    }
                }
            }
            catch (Exception)
            {
                // Never retain a config that failed its telemetry projection as a normal
                // snapshot, nor affect serving. Disable rather than emit misleading epochs.
                Disable();
            }
        }

        public void MarkHostReady()
        {
            lock (_sync)
            {
                _hostReady = true;
                TryReady();
            }
        }

        public void ConfigurationChangeFailed(TelemetryFailureStage stage = TelemetryFailureStage.Unknown)
        {
            lock (_sync)
            {
                if (_enabled)
                {
                    Emit("dab.engine.configuration_change_failed", _configuration.Epoch,
                        ImmutableDictionary<string, string>.Empty
                            .Add("failure_stage", Wire(stage))
                            .Add("failure_category", "configuration"));
                }
            }
        }

        public void StartupFailed(TelemetryFailureStage stage = TelemetryFailureStage.Initialization)
        {
            lock (_sync)
            {
                if (_enabled && !_ready && !_startupFailed)
                {
                    _startupFailed = true;
                    Emit("dab.engine.startup_failed", _configuration.Epoch, ImmutableDictionary<string, string>.Empty
                        .Add("failure_stage", Wire(stage))
                        .Add("failure_category", "initialization"));
                }
            }
        }

        public EngineTelemetryRequestScope BeginRequest(EngineTelemetryApi api, EngineTelemetryTransport transport, EngineTelemetryRole role, bool eligible = true)
        {
            lock (_sync)
            {
                bool ready = _enabled && _ready;
                EngineTelemetryRequestScope scope = new(
                    this, _request.Value, _configuration, ready ? _config : null,
                    api, transport, role, eligible: ready && eligible,
                    started: ready ? _clock.GetTimestamp() : 0);
                if (ready)
                {
                    _request.Value = scope;
                }

                return scope;
            }
        }

        public EngineTelemetryMeasurementScope? BeginOperation(string? entityName, EngineTelemetryOperation operation)
        {
            EngineTelemetryRequestScope? request = ActiveRequest();
            if (request is null)
            {
                return null;
            }

            int depth = _operationDepth.Value;
            _operationDepth.Value = depth + 1;
            if (depth > 0)
            {
                return new(_ => { }, () => _operationDepth.Value = depth);
            }

            EngineTelemetryProvider provider = EngineTelemetryProvider.Unknown;
            EngineTelemetryObject objectType = EngineTelemetryObject.Unknown;
            try
            {
                if (entityName is not null && request.Config!.Entities.TryGetValue(entityName, out Entity? entity))
                {
                    DataSource source = request.Config.GetDataSourceFromDataSourceName(request.Config.GetDataSourceNameFromEntityName(entityName));
                    provider = Provider(source.DatabaseType);
                    objectType = source.DatabaseType == DatabaseType.CosmosDB_NoSQL ? EngineTelemetryObject.Document : entity.Source.Type switch
                    {
                        EntitySourceType.Table => EngineTelemetryObject.Table,
                        EntitySourceType.View => EngineTelemetryObject.View,
                        EntitySourceType.StoredProcedure => EngineTelemetryObject.StoredProcedure,
                        _ => EngineTelemetryObject.Unknown
                    };
                    if (objectType == EngineTelemetryObject.StoredProcedure)
                    {
                        operation = EngineTelemetryOperation.Execute;
                    }
                }
            }
            catch (Exception)
            {
                // Missing metadata remains unknown; never perform a metadata query for telemetry.
            }

            return new(outcome =>
            {
                if (IsEnabled)
                {
                    _aggregator.RecordOperation(request.Configuration, request.Api, operation, provider, objectType, outcome);
                }
            }, () => _operationDepth.Value = depth);
        }

        public EngineTelemetryMeasurementScope? BeginDatabaseAttempt(DatabaseType? provider)
        {
            EngineTelemetryRequestScope? request = ActiveRequest();
            return request is null ? null : new(outcome =>
            {
                if (IsEnabled)
                {
                    _aggregator.RecordDatabaseAttempt(request.Configuration,
                        provider is DatabaseType knownProvider ? Provider(knownProvider) : EngineTelemetryProvider.Unknown, outcome);
                }
            });
        }

        public EngineTelemetryMeasurementScope? BeginEmbedding()
        {
            EngineTelemetryRequestScope? request = ActiveRequest();
            return request is null ? null : new(outcome =>
            {
                if (IsEnabled)
                {
                    _aggregator.RecordEmbedding(request.Configuration, request.Api, outcome);
                }
            });
        }

        public void RecordCacheLookup(EngineTelemetryCacheLayer layer, EngineTelemetryCacheResult result)
        {
            EngineTelemetryRequestScope? request = ActiveRequest();
            if (request is not null)
            {
                _aggregator.RecordCacheLookup(request.Configuration, layer, result);
            }
        }

        internal void CompleteRequest(EngineTelemetryRequestScope request, EngineTelemetryOutcome outcome, int? status)
        {
            lock (_sync)
            {
                if (!_enabled || !_ready || !request.IsEligible || request.Config is null)
                {
                    return;
                }

                TimeSpan duration = _clock.GetElapsedTime(request.Started, _clock.GetTimestamp());
                _aggregator.RecordRequest(request.Configuration, request.Api, request.Transport, request.Role, outcome, duration);
                if (request.Transport == EngineTelemetryTransport.Http)
                {
                    _aggregator.RecordHttpOutcome(request.Configuration, request.Api, status);
                }

                ImmutableDictionary<string, string> milestone = ImmutableDictionary<string, string>.Empty
                    .Add("api", Wire(request.Api))
                    .Add("transport", Wire(request.Transport))
                    .Add("outcome", Wire(outcome))
                    .Add("since_ready_ms", Milliseconds(_clock.GetElapsedTime(_readyAt, _clock.GetTimestamp())));
                if (!_firstServed)
                {
                    _firstServed = true;
                    Emit("dab.engine.first_request_served", request.Configuration.Epoch, milestone);
                }

                if (!_firstSuccess && outcome == EngineTelemetryOutcome.Success)
                {
                    _firstSuccess = true;
                    Emit("dab.engine.first_successful_request", request.Configuration.Epoch, milestone);
                }
            }
        }

        internal void RestoreRequest(EngineTelemetryRequestScope scope, EngineTelemetryRequestScope? previous)
        {
            if (ReferenceEquals(_request.Value, scope))
            {
                _request.Value = previous;
            }
        }

        public void Tick()
        {
            try
            {
                lock (_sync)
                {
                    if (!_enabled)
                    {
                        return;
                    }

                    EmitSummaries(_aggregator.DrainCompletedWindows());
                    long now = _clock.GetTimestamp();
                    if (_clock.GetElapsedTime(_lastHeartbeat, now) >= TimeSpan.FromMinutes(10))
                    {
                        _lastHeartbeat = now;
                        Emit("dab.engine.heartbeat", _configuration.Epoch, ImmutableDictionary<string, string>.Empty
                            .Add("run_state", _ready ? "ready" : _startupFailed ? "startup_failed" : "initializing")
                            .Add("uptime", UptimeBucket(_clock.GetElapsedTime(_started, now))));
                    }
                }
            }
            catch (Exception)
            {
                Disable();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Disable();
                return Task.CompletedTask;
            }

            lock (_sync)
            {
                if (!_enabled)
                {
                    if (_stopTask is not null && cancellationToken.CanBeCanceled)
                    {
                        // Reuse the delivery deadline, but let every host stop caller cancel
                        // its pending drain instead of silently ignoring later cancellation.
                        return _delivery!.StopAsync(cancellationToken);
                    }

                    return _stopTask ?? Task.CompletedTask;
                }

                _timer?.Dispose();
                _timer = null;
                EmitSummaries(_aggregator.Complete());
                Emit("dab.engine.stopped", _configuration.Epoch, ImmutableDictionary<string, string>.Empty
                    .Add("reason", "graceful_shutdown")
                    .Add("uptime", UptimeBucket(_clock.GetElapsedTime(_started, _clock.GetTimestamp()))));
                _enabled = false;
                _config = null;
                _identityResolution = null;
                return _stopTask = _delivery?.StopAsync(cancellationToken) ?? Task.CompletedTask;
            }
        }

        public void Disable()
        {
            lock (_sync)
            {
                _enabled = false;
                _timer?.Dispose();
                _timer = null;
                _aggregator.Disable();
                _delivery?.Disable();
                _config = null;
                _identityResolution = null;
                _snapshot = ImmutableDictionary<string, string>.Empty;
            }
        }

        public void Dispose() => Disable();

        private EngineTelemetryRequestScope? ActiveRequest()
        {
            EngineTelemetryRequestScope? request = _request.Value;
            if (!IsEnabled || request?.IsEligible != true || request.IsCompleted || request.IsDisposed || request.Config is null)
            {
                return null;
            }

            return request;
        }

        private void TryReady()
        {
            if (!_enabled || _ready || _startupFailed || !_hostReady || _config is null || _configuration.Epoch == 0
                || !(_config.IsRestEnabled || _config.IsGraphQLEnabled || _config.IsMcpEnabled))
            {
                return;
            }

            _ready = true;
            _readyAt = _clock.GetTimestamp();
            Emit("dab.engine.ready", _configuration.Epoch, _snapshot.Add("startup_ms", Milliseconds(_clock.GetElapsedTime(_started, _readyAt))));
        }

        private void EmitSummaries(EngineTelemetryDrain drain)
        {
            foreach (EngineTelemetryWindow window in drain.Windows)
            {
                foreach (EngineTelemetrySeries series in window.Series)
                {
                    ImmutableDictionary<string, string>.Builder data = ImmutableDictionary.CreateBuilder<string, string>();
                    data.Add("window_start", window.Start.ToString("O", CultureInfo.InvariantCulture));
                    data.Add("window_end", window.End.ToString("O", CultureInfo.InvariantCulture));
                    data.Add("final", window.IsFinal ? "true" : "false");
                    data.Add("family", Wire(series.Dimensions.Measurement));
                    data.Add("api", Wire(series.Dimensions.Api));
                    data.Add("transport", Wire(series.Dimensions.Transport));
                    data.Add("role_class", Wire(series.Dimensions.Role));
                    data.Add("operation", Wire(series.Dimensions.Operation));
                    data.Add("provider", Wire(series.Dimensions.Provider));
                    data.Add("object_type", Wire(series.Dimensions.ObjectType));
                    data.Add("cache_layer", Wire(series.Dimensions.CacheLayer));
                    data.Add("cache_result", Wire(series.Dimensions.CacheResult));
                    data.Add("http_status_class", Wire(series.Dimensions.HttpStatusClass));
                    data.Add("count", Number(series.Count));
                    data.Add("capped", series.IsCapped ? "true" : "false");
                    // Cosmos SDK hides internal retries and cache background attribution can be
                    // unavailable; never present those absent observations as exact zero coverage.
                    data.Add("database_attempt_coverage", "sql_commands_only");
                    data.Add("cache_coverage", "request_context_observed");
                    if (series.Outcomes is EngineTelemetryOutcomeCounts outcomes)
                    {
                        data.Add("unknown", Number(outcomes.Unknown));
                        data.Add("success", Number(outcomes.Success));
                        data.Add("failure", Number(outcomes.Failure));
                        data.Add("partial_failure", Number(outcomes.PartialFailure));
                        data.Add("canceled", Number(outcomes.Canceled));
                    }

                    if (series.Latency is EngineTelemetryHistogram histogram)
                    {
                        data.Add("latency_schema", EngineTelemetryHistogram.BUCKET_SCHEMA);
                        data.Add("latency_bounds_ms", JsonSerializer.Serialize(EngineTelemetryHistogram.UpperBoundsMilliseconds));
                        data.Add("latency_buckets", JsonSerializer.Serialize(histogram.Buckets));
                        data.Add("timed_count", Number(histogram.TimedCount));
                        data.Add("latency_complete", histogram.IsComplete ? "true" : "false");
                    }

                    Emit("dab.engine.usage_summary", series.ConfigurationEpoch, data.ToImmutable());
                }

                if (window.SeriesCapacityDrops > 0 || window.CounterCapacityDrops > 0 || window.ClockRegressionDrops > 0)
                {
                    Emit("dab.engine.usage_summary", 0, ImmutableDictionary<string, string>.Empty.Add("family", "collection_loss")
                        .Add("window_start", window.Start.ToString("O", CultureInfo.InvariantCulture))
                        .Add("window_end", window.End.ToString("O", CultureInfo.InvariantCulture))
                        .Add("series_capacity_drops", Number(window.SeriesCapacityDrops))
                        .Add("counter_capacity_drops", Number(window.CounterCapacityDrops))
                        .Add("clock_regression_drops", Number(window.ClockRegressionDrops))
                        .Add("capped", window.LossCountsCapped ? "true" : "false"));
                }
            }

            if (drain.DroppedWindows > 0)
            {
                Emit("dab.engine.usage_summary", 0, ImmutableDictionary<string, string>.Empty.Add("family", "collection_loss")
                    .Add("dropped_windows", Number(drain.DroppedWindows))
                    .Add("dropped_measurements", Number(drain.DroppedMeasurements))
                    .Add("capped", drain.LossCountsCapped ? "true" : "false"));
            }
        }

        private void Emit(string name, long epoch, ImmutableDictionary<string, string> properties)
        {
            if (!_enabled || _sequence == long.MaxValue)
            {
                return;
            }

            properties = properties.SetItems(_context);
            if (_identity is not null)
            {
                properties = properties.Add("dab_api_id", _identity.ApiId.ToString("D"))
                    .Add("dab_api_id_stability", _identity.Stability);
            }

            properties = properties.Add("sender_dropped_events", Number(_delivery?.DroppedEvents ?? 0));
            _delivery?.TryEnqueue(new(Guid.NewGuid(), _sessionId, ++_sequence, _clock.GetUtcNow().ToUniversalTime(), epoch, name, properties));
        }

        public static EngineTelemetryRole ClassifyRole(string? role, bool authenticated = false) => role switch
        {
            null or "" => authenticated ? EngineTelemetryRole.Authenticated : EngineTelemetryRole.Anonymous,
            _ when string.Equals(role, "anonymous", StringComparison.OrdinalIgnoreCase) => EngineTelemetryRole.Anonymous,
            _ when string.Equals(role, "authenticated", StringComparison.OrdinalIgnoreCase) => EngineTelemetryRole.Authenticated,
            _ => EngineTelemetryRole.Custom
        };

        private static EngineTelemetryProvider Provider(DatabaseType type) => type switch
        {
            DatabaseType.MSSQL => EngineTelemetryProvider.MsSql,
            DatabaseType.DWSQL => EngineTelemetryProvider.DwSql,
            DatabaseType.PostgreSQL => EngineTelemetryProvider.PostgreSql,
            DatabaseType.MySQL => EngineTelemetryProvider.MySql,
            DatabaseType.CosmosDB_NoSQL => EngineTelemetryProvider.CosmosDb,
            _ => EngineTelemetryProvider.Unknown
        };

        private static void ShowNotice()
        {
            using StreamWriter writer = new(Console.OpenStandardError(), new System.Text.UTF8Encoding(false), leaveOpen: true);
            writer.WriteLine("DAB synthetic product telemetry is enabled for validation: categorical configuration and aggregate usage are sent to the selected test destination. Disable with DAB_TELEMETRY_OPT_OUT=1. Fields: docs/telemetry.md (product repository). No request content is collected.");
        }
    }

}

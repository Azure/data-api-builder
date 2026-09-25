// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using Azure.DataApiBuilder.Config.Telemetry;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    internal enum CliTelemetryOutcome
    {
        Success,
        ParseFailure,
        ValidationFailure,
        ExecutionFailure,
        Canceled,
        Unknown
    }

    internal enum CliTelemetryFailureCategory
    {
        None,
        Arguments,
        Configuration,
        Storage,
        Initialization,
        Execution,
        Canceled,
        Unknown
    }

    /// <summary>
    /// One CLI invocation's best-effort product telemetry. Disabled unless explicitly enabled for
    /// synthetic validation; the CLI bootstrap must separately validate destination/SDK policy.
    /// Only three event names are produced: first_run, command and engine_launch. No disposal,
    /// cancellation or stop path manufactures a command or a final event.
    /// </summary>
    internal sealed class CliTelemetrySession : IProductTelemetryControl, IDisposable
    {
        private const int MAX_LAUNCHES = 16;
        private const int MAX_OPTIONS = 96;
        private const int MAX_OPTION_KEY_LENGTH = 100;
        private readonly object _sync = new();
        private readonly TimeProvider? _clock;
        private readonly long _started;
        private readonly ProductTelemetryDelivery<CliTelemetryEvent>? _delivery;
        private readonly Func<IProductTelemetryExporter<IProductTelemetryEvent>>? _exporterFactory;
        private readonly ImmutableDictionary<string, string>? _context;
        private readonly CliTelemetryInstallation? _installation;
        private readonly Func<string?, EngineTelemetryIdentity>? _createIdentity;
        private readonly Func<string?, EngineTelemetryIdentity?>? _lookupIdentity;
        private bool _enabled;
        private bool _completed;
        private bool _revoked;
        private bool _normalStopRequested;
        private long _sequence;
        // Ordinary handoffs and reservations share one lifetime budget, including abandoned
        // tickets. Releasing a ticket must not permit an unbounded series of helper workers.
        private int _launches;
        private HashSet<ProductTelemetryDelivery<CliTelemetryEvent>>? _deferredDeliveries;
        private long _deferredDroppedEvents;
        private Failure? _failure;
        private bool _engineStartupFailed;
        private Task? _stopTask;

        // This exact, caller-resolved root is LOCAL correlation only. Do not normalize it, read
        // config contents, follow constituent files, or put it (or a hash of it) in any envelope.
        // Retain only one target, not an unbounded per-path cache. Ambiguity is irreversible.
        private string? _configurationPath;
        private bool _configurationObserved;
        private bool _configurationAmbiguous;
        private EngineTelemetryIdentity? _configurationIdentity;
        private Lazy<EngineTelemetryIdentity?>? _lookupResolution;
        private Lazy<EngineTelemetryIdentity?>? _createResolution;

        private CliTelemetrySession()
        {
        }

        private CliTelemetrySession(Func<IProductTelemetryExporter<IProductTelemetryEvent>> exporterFactory,
            TimeProvider clock, long started, ImmutableDictionary<string, string> context,
            CliTelemetryInstallation installation, Func<string?, EngineTelemetryIdentity> createIdentity,
            Func<string?, EngineTelemetryIdentity?> lookupIdentity)
        {
            _clock = clock;
            _started = started;
            _context = context;
            _installation = installation;
            _createIdentity = createIdentity;
            _lookupIdentity = lookupIdentity;
            _exporterFactory = exporterFactory;
            SessionId = Guid.NewGuid();
            _delivery = new(() => exporterFactory(), capacity: 256, maxAttempts: 3,
                flushTimeout: TimeSpan.FromSeconds(2), timeProvider: clock);
            _enabled = true;
        }

        internal static CliTelemetrySession Create(
            Func<IProductTelemetryExporter<IProductTelemetryEvent>>? exporterFactory = null,
            bool enableSyntheticCollection = false,
            TimeProvider? clock = null,
            Func<string, string?>? readEnvironmentVariable = null,
            Action? showNotice = null,
            Func<CliTelemetryInstallation>? resolveInstallation = null,
            Func<string?, EngineTelemetryIdentity>? createIdentity = null,
            Func<string?, EngineTelemetryIdentity?>? lookupIdentity = null)
        {
            // No environment, notice, clock, identity, context or delivery work on this path.
            if (!enableSyntheticCollection || exporterFactory is null)
            {
                return new();
            }

            CliTelemetrySession? session = null;
            try
            {
                Func<string, string?> readEnvironment = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
                if (ProductTelemetryPolicy.IsOptedOut(readEnvironment(ProductTelemetryPolicy.OPT_OUT_ENV_VAR)))
                {
                    return new();
                }

                // Capture the umbrella policy once. The identity stores must not re-read a later
                // process value and accidentally change this invocation's policy. No raw opt-out
                // value needs to be retained. Disable is the explicit in-process veto.
                string? ReadStartupEnvironment(string name) => name == ProductTelemetryPolicy.OPT_OUT_ENV_VAR
                    ? null : readEnvironment(name);

                // Fail closed if the required notice cannot be shown. Never use stdout, a host
                // logger, or persisted notice state (including in MCP stdio invocations).
                (showNotice ?? ShowNotice)();
                TimeProvider timeProvider = clock ?? TimeProvider.System;
                long started = timeProvider.GetTimestamp();
                ImmutableDictionary<string, string> context = EngineTelemetryContext.Create("unknown", ReadStartupEnvironment)
                    .Remove("execution_mode")
                    .Remove("launcher")
                    .SetItem("packaging", "unknown")
                    .SetItem("install_channel", "unknown");
                CliTelemetryInstallation installation = NormalizeInstallation(
                    resolveInstallation is null ? CliTelemetryInstallationStore.Resolve(ReadStartupEnvironment) : resolveInstallation());
                context = context.Add("dab_installation_id_stability", installation.Stability);
                if (installation.InstallationId is Guid installationId)
                {
                    context = context.Add("dab_installation_id", installationId.ToString("D"));
                }

                session = new(exporterFactory, timeProvider, started, context, installation,
                    createIdentity ?? (path => EngineTelemetryIdentityStore.Resolve(path, ReadStartupEnvironment)),
                    lookupIdentity ?? (path => EngineTelemetryIdentityStore.Lookup(path, ReadStartupEnvironment)));
                if (installation.Stability == "newly_saved")
                {
                    lock (session._sync)
                    {
                        session.Emit("dab.cli.first_run", ImmutableDictionary<string, string>.Empty);
                    }
                }

                return session;
            }
            catch (Exception)
            {
                // Telemetry-only failures never become application failures or diagnostic logs.
                session?.Disable();
                return new();
            }
        }

        public bool IsEnabled => Volatile.Read(ref _enabled);
        public Guid SessionId { get; }

        internal bool HasEngineStartupFailed => Volatile.Read(ref _engineStartupFailed);

        internal void ObserveEngineStartupFailure()
        {
            if (IsEnabled)
            {
                Volatile.Write(ref _engineStartupFailed, true);
            }
        }

        // Normal shutdown closes only the invocation's ordinary collector. A previously
        // reserved helper still uses the captured policy unless explicitly revoked.
        internal bool CanBeginReservedLaunch => _exporterFactory is not null && !Volatile.Read(ref _revoked);

        public bool HasFailure
        {
            get
            {
                lock (_sync)
                {
                    return _failure is not null;
                }
            }
        }

        public CliTelemetryOutcome FailureOutcome
        {
            get
            {
                lock (_sync)
                {
                    return _failure?.Outcome ?? CliTelemetryOutcome.Unknown;
                }
            }
        }

        public CliTelemetryFailureCategory FailureCategory
        {
            get
            {
                lock (_sync)
                {
                    return _failure?.Category ?? CliTelemetryFailureCategory.None;
                }
            }
        }

        /// <summary>
        /// Look up only an already resolved root's existing identity. Unknown roots and different
        /// exact roots permanently suppress command linkage, even if their sidecars share an ID.
        /// Missing identity for one known root can be filled by a later eligible create/launch.
        /// </summary>
        public void ObserveConfiguration(string? path)
        {
            if (!IsEnabled)
            {
                return;
            }

            try
            {
                Lazy<EngineTelemetryIdentity?> resolution;
                lock (_sync)
                {
                    if (!_enabled || _completed)
                    {
                        return;
                    }

                    ObserveTarget(path);
                    if (_configurationAmbiguous || _createResolution is not null)
                    {
                        return;
                    }

                    resolution = _lookupResolution ??= new(() => NormalizeIdentity(_lookupIdentity!(path)));
                }

                EngineTelemetryIdentity? identity = resolution.Value;
                lock (_sync)
                {
                    // A slow lookup must not replace an eligible create, including an ephemeral
                    // identity, or forget a different target observed while I/O was in progress.
                    if (_enabled && !_completed && !_configurationAmbiguous && _createResolution is null)
                    {
                        _configurationIdentity = identity;
                    }
                }
            }
            catch (Exception)
            {
                Disable();
            }
        }

        /// <summary>
        /// Called only after the CLI successfully writes a configuration. No success is inferred
        /// here. A missing path is a no-op, never a request to create identity in the current directory.
        /// </summary>
        public void ConfigurationCreated(string? path)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                Lazy<EngineTelemetryIdentity?> resolution;
                lock (_sync)
                {
                    if (!_enabled || _completed)
                    {
                        return;
                    }

                    ObserveTarget(path);
                    resolution = GetCreateResolution(path);
                }

                EngineTelemetryIdentity? identity = resolution.Value;
                lock (_sync)
                {
                    if (_enabled && !_completed && !_configurationAmbiguous)
                    {
                        _configurationIdentity = identity;
                    }
                }
            }
            catch (Exception)
            {
                Disable();
            }
        }

        /// <summary>
        /// Called at an actual engine handoff, never when a start/export command merely parses.
        /// Enqueues engine_launch before returning its immutable bridge, not an export receipt.
        /// Identity I/O occurs outside the gate and cannot revive a stopped/completed CLI.
        /// </summary>
        public ProductTelemetryLaunchContext? BeginEngineLaunch(string? rootPath, CliTelemetryLaunchSource source)
        {
            if (!IsEnabled || source is not (CliTelemetryLaunchSource.StartWeb
                or CliTelemetryLaunchSource.StartStdio or CliTelemetryLaunchSource.ExportGraphQL))
            {
                return null;
            }

            try
            {
                Lazy<EngineTelemetryIdentity?> resolution;
                lock (_sync)
                {
                    if (!_enabled || _completed)
                    {
                        return null;
                    }

                    // Even a capped handoff is evidence of another target. Suppress ambiguous
                    // command linkage without doing more identity I/O or emitting more launches.
                    ObserveTarget(rootPath);
                    if (_launches >= MAX_LAUNCHES)
                    {
                        return null;
                    }

                    // Reserve before I/O so concurrent handoffs cannot exceed the bound. Even
                    // unfinished operations contribute target ambiguity before Complete can run.
                    _launches++;
                    resolution = GetCreateResolution(rootPath);
                }

                EngineTelemetryIdentity? identity = resolution.Value;
                lock (_sync)
                {
                    if (!_enabled || _completed)
                    {
                        return null;
                    }

                    if (!_configurationAmbiguous)
                    {
                        _configurationIdentity = identity;
                    }

                    ProductTelemetryLaunchContext launch = new(Guid.NewGuid(), SessionId, _installation!, identity, source);
                    return Emit("dab.cli.engine_launch", LaunchProperties(launch), identity) ? launch : null;
                }
            }
            catch (Exception)
            {
                Disable();
                return null;
            }
        }

        /// <summary>
        /// Reserve observation of a helper scheduled by a live invocation, not an engine launch.
        /// No identity, clock, event, delivery worker or exporter is created by a reservation.
        /// Only this explicit one-shot ownership can survive Complete and normal Stop/Dispose.
        /// </summary>
        internal CliTelemetryLaunchReservation? ReserveEngineLaunch(CliTelemetryLaunchSource source)
        {
            if (!IsEnabled || source is not (CliTelemetryLaunchSource.StartWeb
                or CliTelemetryLaunchSource.StartStdio or CliTelemetryLaunchSource.ExportGraphQL))
            {
                return null;
            }

            try
            {
                lock (_sync)
                {
                    if (!_enabled || _completed || _revoked || _launches >= MAX_LAUNCHES)
                    {
                        return null;
                    }

                    _launches++;
                    return new(this, source);
                }
            }
            catch (Exception)
            {
                Disable();
                return null;
            }
        }

        // Called only by a consumed reservation, at the preflight-approved handoff. Identity
        // resolution must remain outside _sync so neither completion nor revocation waits on it.
        internal ProductTelemetryLaunchContext? BeginReservedEngineLaunch(string? rootPath, CliTelemetryLaunchSource source)
        {
            Func<IProductTelemetryExporter<IProductTelemetryEvent>>? exporterFactory = _exporterFactory;
            if (exporterFactory is null || !CanBeginReservedLaunch)
            {
                return null;
            }

            ProductTelemetryDelivery<CliTelemetryEvent>? delivery = null;
            try
            {
                Lazy<EngineTelemetryIdentity?> resolution;
                lock (_sync)
                {
                    if (_revoked)
                    {
                        return null;
                    }

                    if (_enabled && !_completed)
                    {
                        ObserveTarget(rootPath);
                    }

                    // Reuse a retained same-root create (including ephemeral evidence). Normal
                    // Stop forgets that cache; otherwise resolve this actual root once per ticket.
                    resolution = GetCreateResolution(rootPath);
                }

                EngineTelemetryIdentity? identity = resolution.Value;
                lock (_sync)
                {
                    if (_revoked)
                    {
                        return null;
                    }

                    if (_enabled && !_completed && !_configurationAmbiguous)
                    {
                        _configurationIdentity = identity;
                    }

                    ProductTelemetryLaunchContext launch = new(Guid.NewGuid(), SessionId, _installation!, identity, source);
                    // Always acquire an independent worker-owned lease, never the CLI's or the
                    // engine's exporter instance. Factory/SDK/network work stays on the worker.
                    delivery = new(() => exporterFactory(), capacity: 1, maxAttempts: 3,
                        flushTimeout: TimeSpan.FromSeconds(2), timeProvider: _clock!);
                    (_deferredDeliveries ??= []).Add(delivery);
                    return Emit("dab.cli.engine_launch", LaunchProperties(launch), identity, delivery) ? launch : null;
                }
            }
            catch (Exception)
            {
                Disable();
                return null;
            }
            finally
            {
                if (delivery is not null)
                {
                    // Observes all failures and releases tracking after the bounded drain.
                    // Neither the engine handoff nor normal CLI shutdown joins this task.
                    _ = StopDeferredDeliveryAsync(delivery);
                }
            }
        }

        /// <summary>
        /// Remember only the first typed failure for the command wrapper to classify its result.
        /// Success is not a failure; no exception, diagnostic text or event is retained here.
        /// </summary>
        public void MarkFailure(CliTelemetryOutcome outcome, CliTelemetryFailureCategory failureCategory)
        {
            if (!IsEnabled || outcome == CliTelemetryOutcome.Success)
            {
                return;
            }

            try
            {
                lock (_sync)
                {
                    if (_enabled && !_completed && _failure is null)
                    {
                        _failure = new(NormalizeOutcome(outcome), NormalizeFailureCategory(failureCategory));
                    }
                }
            }
            catch (Exception)
            {
                Disable();
            }
        }

        /// <summary>
        /// Complete at most once. The caller supplies its final typed classification (it can use
        /// the first-failure properties); this method does not override that result. Options are
        /// the grammar adapter's canonical option_* keys and literal "true" presence flags ONLY.
        /// Core independently approves each fixed key; syntactic validation is not approval.
        /// </summary>
        public void Complete(string command, string control, ImmutableDictionary<string, string> options,
            CliTelemetryOutcome outcome, CliTelemetryFailureCategory failureCategory = CliTelemetryFailureCategory.None)
        {
            if (!IsEnabled)
            {
                return;
            }

            try
            {
                lock (_sync)
                {
                    if (!_enabled || _completed)
                    {
                        return;
                    }

                    _completed = true;
                    TimeSpan elapsed = _clock!.GetElapsedTime(_started, _clock.GetTimestamp());
                    ImmutableDictionary<string, string>.Builder properties = ImmutableDictionary.CreateBuilder<string, string>();
                    properties.Add("command", command is "init" or "add" or "update" or "start" or "validate" or "export"
                        or "add-telemetry" or "configure" or "auto-config" or "auto-config-simulate" or "appname" ? command : "unknown");
                    properties.Add("subcommand", "none");
                    properties.Add("control", control is "help" or "version" ? control : "none");
                    properties.Add("outcome", Wire(outcome));
                    properties.Add("failure_category", Wire(failureCategory));
                    properties.Add("duration_ms", Math.Max(0, elapsed.Ticks / TimeSpan.TicksPerMillisecond).ToString(CultureInfo.InvariantCulture));
                    if (options is not null && options.Count <= MAX_OPTIONS)
                    {
                        foreach ((string key, string value) in options)
                        {
                            if (key.Length <= MAX_OPTION_KEY_LENGTH
                                && key.StartsWith("option_", StringComparison.Ordinal)
                                && key.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
                                && string.Equals(value, "true", StringComparison.Ordinal)
                                && IsApprovedOption(key))
                            {
                                properties.Add(key, "true");
                            }
                        }
                    }

                    Emit("dab.cli.command", properties.ToImmutable(), _configurationAmbiguous ? null : _configurationIdentity);
                }
            }
            catch (Exception)
            {
                Disable();
            }
        }

        /// <summary>Close ordinary collection, not reserved helpers, with one bounded drain. Emits nothing.</summary>
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Disable();
                return Task.CompletedTask;
            }

            try
            {
                lock (_sync)
                {
                    _normalStopRequested = true;
                    _enabled = false;
                    ForgetConfiguration();
                    if (_delivery is null)
                    {
                        return Task.CompletedTask;
                    }

                    _stopTask ??= StopDeliveryAsync(_delivery, CancellationToken.None);
                    // Later callers can still cancel an already pending shared drain.
                    return cancellationToken.CanBeCanceled ? StopDeliveryAsync(_delivery, cancellationToken) : _stopTask;
                }
            }
            catch (Exception)
            {
                Disable();
                return Task.CompletedTask;
            }
        }

        public void Disable()
        {
            lock (_sync)
            {
                _revoked = true;
                _enabled = false;
                ForgetConfiguration();
                try
                {
                    _delivery?.Disable();
                }
                catch (Exception)
                {
                    // Best effort even for telemetry cleanup; never affect the caller.
                }

                if (_deferredDeliveries is not null)
                {
                    foreach (ProductTelemetryDelivery<CliTelemetryEvent> delivery in _deferredDeliveries)
                    {
                        try
                        {
                            delivery.Disable();
                        }
                        catch (Exception)
                        {
                            // Each owned worker is revoked independently, without a final flush.
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (!_normalStopRequested)
                {
                    Disable();
                }
            }
        }

        // Caller holds _sync. Comparing exact caller-resolved paths avoids guessing about CWD,
        // filesystem aliases, case sensitivity, merges or equivalence from copied sidecar IDs.
        private void ObserveTarget(string? path)
        {
            if (!_configurationObserved)
            {
                _configurationObserved = true;
                _configurationPath = string.IsNullOrWhiteSpace(path) ? null : path;
            }

            if (string.IsNullOrWhiteSpace(path) || !string.Equals(path, _configurationPath, StringComparison.Ordinal))
            {
                _configurationAmbiguous = true;
                _configurationIdentity = null;
            }
        }

        // Caller holds _sync, but evaluates the Lazy OUTSIDE it. Reuse the first exact target's
        // eligible create, including an ephemeral result. A lookup is never promoted to a create
        // for read-only commands. Other launch targets get their own result, not the first ID.
        private Lazy<EngineTelemetryIdentity?> GetCreateResolution(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && string.Equals(path, _configurationPath, StringComparison.Ordinal))
            {
                return _createResolution ??= new(() => NormalizeIdentity(_createIdentity!(path)));
            }

            return new(() => NormalizeIdentity(_createIdentity!(path)));
        }

        private void ForgetConfiguration()
        {
            _configurationPath = null;
            _configurationIdentity = null;
            _lookupResolution = null;
            _createResolution = null;
        }

        // Caller holds _sync. Only immutable snapshots enter the worker, and each launch supplies
        // its OWN identity even when the command's identity has become ambiguous. Reserved
        // deliveries share the original CLI sequence/context but not its ordinary admission gate.
        private bool Emit(string name, ImmutableDictionary<string, string> properties, EngineTelemetryIdentity? identity = null,
            ProductTelemetryDelivery<CliTelemetryEvent>? deferredDelivery = null)
        {
            if ((deferredDelivery is null ? !_enabled : _revoked) || _sequence == long.MaxValue)
            {
                return false;
            }

            properties = properties.SetItems(_context!);
            if (identity is not null)
            {
                properties = properties.Add("dab_api_id", identity.ApiId.ToString("D"))
                    .Add("dab_api_id_stability", identity.Stability);
            }

            properties = properties.Add("sender_dropped_events", DroppedEvents().ToString(CultureInfo.InvariantCulture));
            DateTimeOffset occurredAt = _clock!.GetUtcNow().ToUniversalTime();
            return (deferredDelivery is null ? _enabled : !_revoked)
                && (deferredDelivery ?? _delivery!).TryEnqueue(new(Guid.NewGuid(), SessionId, ++_sequence, occurredAt, name, properties));
        }

        private static ImmutableDictionary<string, string> LaunchProperties(ProductTelemetryLaunchContext launch)
            => ImmutableDictionary<string, string>.Empty
                .Add("dab_launched_engine_session_id", launch.EngineSessionId.ToString("D"))
                .Add("dab_parent_cli_session_id", launch.ParentCliSessionId.ToString("D"))
                .Add("launch_source", launch.Source switch
                {
                    CliTelemetryLaunchSource.StartWeb => "start_web",
                    CliTelemetryLaunchSource.StartStdio => "start_stdio",
                    _ => "export_graphql"
                });

        // Caller holds _sync. Snapshot both pending and retired senders without double counting.
        // A dropped late launch cannot rewrite a command already enqueued or invent a final event.
        private long DroppedEvents()
        {
            long dropped = AddDroppedEvents(_delivery!.DroppedEvents, _deferredDroppedEvents);
            if (_deferredDeliveries is not null)
            {
                foreach (ProductTelemetryDelivery<CliTelemetryEvent> delivery in _deferredDeliveries)
                {
                    dropped = AddDroppedEvents(dropped, delivery.DroppedEvents);
                }
            }

            return dropped;
        }

        private static long AddDroppedEvents(long first, long second)
            => first >= long.MaxValue - second ? long.MaxValue : first + second;

        private async Task StopDeferredDeliveryAsync(ProductTelemetryDelivery<CliTelemetryEvent> delivery)
        {
            try
            {
                await StopDeliveryAsync(delivery, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    // Dispose only the delivery; its worker exclusively owns exporter cleanup,
                    // even if the deadline elapsed while exporter code ignored cancellation.
                    delivery.Dispose();
                }
                catch (Exception)
                {
                    // The fire-and-forget observer must never surface a cleanup exception.
                }

                lock (_sync)
                {
                    if (_deferredDeliveries?.Remove(delivery) == true)
                    {
                        _deferredDroppedEvents = AddDroppedEvents(_deferredDroppedEvents, delivery.DroppedEvents);
                    }
                }
            }
        }

        private static async Task StopDeliveryAsync(ProductTelemetryDelivery<CliTelemetryEvent> delivery, CancellationToken cancellationToken)
        {
            try
            {
                await delivery.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                try
                {
                    delivery.Disable();
                }
                catch (Exception)
                {
                    // No cleanup failure may escape into CLI shutdown.
                }
            }
        }

        private static CliTelemetryInstallation NormalizeInstallation(CliTelemetryInstallation? installation)
            => installation is { InstallationId: Guid id, Stability: "newly_saved" or "reused" } && id != Guid.Empty
                ? installation : new(null, "unavailable");

        private static EngineTelemetryIdentity? NormalizeIdentity(EngineTelemetryIdentity? identity)
            => identity is { Stability: "newly_saved" or "reused" or "ephemeral" } && identity.ApiId != Guid.Empty ? identity : null;

        private static CliTelemetryOutcome NormalizeOutcome(CliTelemetryOutcome outcome) => outcome switch
        {
            CliTelemetryOutcome.Success or CliTelemetryOutcome.ParseFailure or CliTelemetryOutcome.ValidationFailure
                or CliTelemetryOutcome.ExecutionFailure or CliTelemetryOutcome.Canceled => outcome,
            _ => CliTelemetryOutcome.Unknown
        };

        private static CliTelemetryFailureCategory NormalizeFailureCategory(CliTelemetryFailureCategory category) => category switch
        {
            CliTelemetryFailureCategory.None or CliTelemetryFailureCategory.Arguments or CliTelemetryFailureCategory.Configuration
                or CliTelemetryFailureCategory.Storage or CliTelemetryFailureCategory.Initialization
                or CliTelemetryFailureCategory.Execution or CliTelemetryFailureCategory.Canceled => category,
            _ => CliTelemetryFailureCategory.Unknown
        };

        private static string Wire(CliTelemetryOutcome outcome) => outcome switch
        {
            CliTelemetryOutcome.Success => "success",
            CliTelemetryOutcome.ParseFailure => "parse_failure",
            CliTelemetryOutcome.ValidationFailure => "validation_failure",
            CliTelemetryOutcome.ExecutionFailure => "execution_failure",
            CliTelemetryOutcome.Canceled => "canceled",
            _ => "unknown"
        };

        private static string Wire(CliTelemetryFailureCategory category) => category switch
        {
            CliTelemetryFailureCategory.None => "none",
            CliTelemetryFailureCategory.Arguments => "arguments",
            CliTelemetryFailureCategory.Configuration => "configuration",
            CliTelemetryFailureCategory.Storage => "storage",
            CliTelemetryFailureCategory.Initialization => "initialization",
            CliTelemetryFailureCategory.Execution => "execution",
            CliTelemetryFailureCategory.Canceled => "canceled",
            _ => "unknown"
        };

        // Reviewed OptionAttribute long names from Cli.Options and Cli.Commands, projected by
        // CliTelemetryCommand: option_ + name.Replace('-', '_').Replace('.', '_'); the legacy
        // LogLevel spelling maps to log-level. Core cannot reference the CLI assembly. Deliberately
        // duplicate approval here rather than accepting arbitrary syntactically valid caller keys.
        // New CLI options require a deliberate review of BOTH allowlists. No option values qualify.
        private static bool IsApprovedOption(string key) => key switch
        {
            // Common and init.
            "option_config" or "option_database_type" or "option_connection_string"
                or "option_cosmosdb_nosql_database" or "option_cosmosdb_nosql_container"
                or "option_graphql_schema" or "option_set_session_context" or "option_host_mode"
                or "option_cors_origin" or "option_auth_provider" or "option_auth_audience"
                or "option_auth_issuer" or "option_rest_path" or "option_runtime_base_route"
                or "option_rest_disabled" or "option_graphql_path" or "option_graphql_disabled"
                or "option_mcp_path" or "option_mcp_disabled" or "option_rest_enabled"
                or "option_graphql_enabled" or "option_mcp_enabled" or "option_rest_request_body_strict"
                or "option_graphql_multiple_mutations_create_enabled" or "option_mcp_aggregate_records_query_timeout"
                // Entity, add and update.
                or "option_source" or "option_permissions" or "option_source_type" or "option_source_params"
                or "option_source_key_fields" or "option_rest" or "option_rest_methods" or "option_graphql"
                or "option_graphql_operation" or "option_fields_include" or "option_fields_exclude"
                or "option_policy_request" or "option_policy_database" or "option_cache_enabled"
                or "option_cache_ttl_seconds" or "option_cache_level" or "option_health_enabled"
                or "option_description" or "option_parameters_name" or "option_parameters_description"
                or "option_parameters_required" or "option_parameters_default" or "option_fields_name"
                or "option_fields_alias" or "option_fields_description" or "option_fields_primary_key"
                or "option_mcp_dml_tools" or "option_mcp_custom_tool" or "option_relationship"
                or "option_cardinality" or "option_target_entity" or "option_linking_object"
                or "option_linking_source_fields" or "option_linking_target_fields" or "option_relationship_fields"
                or "option_map"
                // Start, export and customer-configured telemetry sinks.
                or "option_verbose" or "option_log_level" or "option_no_https_redirect" or "option_mcp_stdio"
                or "option_output" or "option_graphql_schema_file" or "option_generate" or "option_sampling_mode"
                or "option_sampling_count" or "option_sampling_partition_key_path" or "option_sampling_days"
                or "option_sampling_group_count" or "option_app_insights_conn_string" or "option_app_insights_enabled"
                or "option_otel_endpoint" or "option_otel_enabled" or "option_otel_headers" or "option_otel_protocol"
                or "option_otel_service_name"
                // Configure: data sources and runtime APIs.
                or "option_data_source_database_type" or "option_data_source_connection_string"
                or "option_data_source_options_database" or "option_data_source_options_container"
                or "option_data_source_options_schema" or "option_data_source_options_set_session_context"
                or "option_data_source_health_name" or "option_data_source_user_delegated_auth_enabled"
                or "option_data_source_user_delegated_auth_database_audience" or "option_data_source_user_delegated_auth_provider"
                or "option_data_source_health_enabled" or "option_data_source_health_threshold_ms" or "option_data_source_files"
                or "option_runtime_graphql_depth_limit" or "option_runtime_graphql_enabled" or "option_runtime_graphql_path"
                or "option_runtime_graphql_allow_introspection" or "option_runtime_graphql_multiple_mutations_create_enabled"
                or "option_runtime_rest_enabled" or "option_runtime_rest_path" or "option_runtime_rest_request_body_strict"
                or "option_runtime_mcp_enabled" or "option_runtime_mcp_path" or "option_runtime_mcp_description"
                or "option_runtime_mcp_dml_tools_enabled" or "option_runtime_mcp_dml_tools_describe_entities"
                or "option_runtime_mcp_dml_tools_create_record" or "option_runtime_mcp_dml_tools_read_records"
                or "option_runtime_mcp_dml_tools_update_record" or "option_runtime_mcp_dml_tools_delete_record"
                or "option_runtime_mcp_dml_tools_execute_entity" or "option_runtime_mcp_dml_tools_aggregate_records"
                or "option_runtime_mcp_dml_tools_aggregate_records_query_timeout"
                // Configure: cache, host, health and key vault.
                or "option_runtime_cache_enabled" or "option_runtime_cache_ttl_seconds"
                or "option_runtime_pagination_max_page_size" or "option_runtime_pagination_default_page_size"
                or "option_runtime_pagination_next_link_relative" or "option_runtime_compression_level"
                or "option_runtime_health_enabled" or "option_runtime_health_cache_ttl_seconds"
                or "option_runtime_health_max_query_parallelism" or "option_runtime_health_roles"
                or "option_runtime_host_mode" or "option_runtime_host_cors_origins" or "option_runtime_host_cors_allow_credentials"
                or "option_runtime_host_authentication_provider" or "option_runtime_host_authentication_jwt_audience"
                or "option_runtime_host_authentication_jwt_issuer" or "option_runtime_host_max_response_size_mb"
                or "option_azure_key_vault_endpoint" or "option_azure_key_vault_retry_policy_mode"
                or "option_azure_key_vault_retry_policy_max_count" or "option_azure_key_vault_retry_policy_delay_seconds"
                or "option_azure_key_vault_retry_policy_max_delay_seconds" or "option_azure_key_vault_retry_policy_network_timeout_seconds"
                // Configure: customer diagnostics and embeddings.
                or "option_runtime_telemetry_azure_log_analytics_enabled" or "option_runtime_telemetry_azure_log_analytics_dab_identifier"
                or "option_runtime_telemetry_azure_log_analytics_flush_interval_seconds"
                or "option_runtime_telemetry_azure_log_analytics_auth_custom_table_name"
                or "option_runtime_telemetry_azure_log_analytics_auth_dcr_immutable_id"
                or "option_runtime_telemetry_azure_log_analytics_auth_dce_endpoint"
                or "option_runtime_telemetry_file_enabled" or "option_runtime_telemetry_file_path"
                or "option_runtime_telemetry_file_rolling_interval" or "option_runtime_telemetry_file_retained_file_count_limit"
                or "option_runtime_telemetry_file_file_size_limit_bytes" or "option_runtime_telemetry_log_level"
                or "option_show_effective_permissions" or "option_runtime_embeddings_enabled"
                or "option_runtime_embeddings_provider" or "option_runtime_embeddings_base_url"
                or "option_runtime_embeddings_api_key" or "option_runtime_embeddings_model"
                or "option_runtime_embeddings_api_version" or "option_runtime_embeddings_dimensions"
                or "option_runtime_embeddings_timeout_ms" or "option_runtime_embeddings_endpoint_enabled"
                or "option_runtime_embeddings_endpoint_roles" or "option_runtime_embeddings_endpoint_path"
                or "option_runtime_embeddings_health_enabled" or "option_runtime_embeddings_health_threshold_ms"
                or "option_runtime_embeddings_health_test_text" or "option_runtime_embeddings_health_expected_dimensions"
                or "option_runtime_embeddings_chunking_enabled" or "option_runtime_embeddings_chunking_size_chars"
                or "option_runtime_embeddings_chunking_overlap_chars" or "option_runtime_embeddings_cache_enabled"
                or "option_runtime_embeddings_cache_ttl_hours" or "option_runtime_embeddings_cache_level_2_enabled"
                or "option_runtime_embeddings_cache_level_2_connection_string"
                // Auto-config, auto-config-simulate and appname.
                or "option_patterns_include" or "option_patterns_exclude" or "option_patterns_name"
                or "option_template_mcp_dml_tools" or "option_template_rest_enabled" or "option_template_graphql_enabled"
                or "option_template_cache_enabled" or "option_template_cache_ttl_seconds" or "option_template_cache_level"
                or "option_template_health_enabled" or "option_decode" => true,
            _ => false
        };

        private static void ShowNotice()
        {
            using StreamWriter writer = new(Console.OpenStandardError(), new System.Text.UTF8Encoding(false), leaveOpen: true);
            writer.WriteLine("DAB synthetic CLI product telemetry is enabled for validation: categorical command usage, option presence and random identifiers are sent to the selected test destination. Disable with DAB_TELEMETRY_OPT_OUT=1. Fields: docs/telemetry.md (product repository). Argument values and configuration contents are not collected.");
        }

        private sealed record Failure(CliTelemetryOutcome Outcome, CliTelemetryFailureCategory Category);
    }
}

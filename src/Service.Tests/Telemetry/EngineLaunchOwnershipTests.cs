// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Service.Telemetry;
using Azure.DataApiBuilder.Service.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HostOptions = Microsoft.Extensions.Hosting.HostOptions;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Exercise the real bootstrap and both owning runners, substituting only the built host,
    /// lifetime and stdio loop. No HTTP listener, metadata, database or live telemetry is used.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    [TestCategory("EngineTelemetry")]
    [TestCategory("CliTelemetry")]
    public class EngineLaunchOwnershipTests
    {
        private HostSettingsScope _settings = null!;

        [TestInitialize]
        public void Initialize() => _settings = new();

        [TestCleanup]
        public void Cleanup() => _settings.Dispose();

        [DataTestMethod]
        [DataRow(false, "success", false)]
        [DataRow(false, "failure", false)]
        [DataRow(false, "cancellation", false)]
        [DataRow(true, "success", false)]
        [DataRow(true, "failure", false)]
        [DataRow(true, "cancellation", false)]
        [DataRow(false, "success", true)]
        [DataRow(false, "failure", true)]
        [DataRow(false, "cancellation", true)]
        [DataRow(true, "success", true)]
        [DataRow(true, "failure", true)]
        [DataRow(true, "cancellation", true)]
        public void StartEngineCoreRunnersDisposeExactlyOnce(bool stdio, string completion, bool throughLaunchSeam)
        {
            RecordingExporter exporter = new();
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter,
                enableSyntheticCollection: true, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => throw new AssertFailedException("A fake host must not accept a configuration."), startTimer: false);
            using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem(), isCliLoader: true,
                logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);
            using TestApplicationLifetime lifetime = new();
            TestStdioServer server = new(lifetime, completion);
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton<IHostApplicationLifetime>(lifetime)
                .AddSingleton<IMcpStdioServer>(server)
                .AddSingleton(loader)
                .AddSingleton(telemetry)
                .BuildServiceProvider();
            TestHost host = new(services, lifetime, completion, loader, telemetry, exporter);
            TestHostBuilder? wrappedBuilder = null;
            int configuredHosts = 0;
            int factories = 0;
            int failuresObserved = 0;
            string[] args = stdio
                ? ["--mcp-stdio", "role:anonymous", "--log-level", "Error"]
                : ["--log-level", "Error"];
            bool result;
            if (throughLaunchSeam)
            {
                EngineLaunchDependencies dependencies = EngineLaunchDependencies.Default with
                {
                    SessionFactory = (actualStdio, context, configPath) =>
                    {
                        factories++;
                        Assert.AreEqual(stdio, actualStdio);
                        Assert.IsNull(context);
                        Assert.IsNull(configPath);
                        return telemetry;
                    },
                    ConfigureHost = WrapHost
                };
                result = Program.StartEngine(args, launchContext: null, dependencies,
                    startupFailureObserved: () => failuresObserved++);
            }
            else
            {
                result = Program.StartEngineCore(args, stdio, mcpRole: stdio ? "anonymous" : null,
                    telemetry, validateUrls: false, configureHost: WrapHost);
            }

            Assert.AreEqual(completion == "success", result);
            Assert.AreEqual(throughLaunchSeam ? 1 : 0, factories);
            Assert.AreEqual(1, configuredHosts);
            Assert.IsNotNull(wrappedBuilder, "The real bootstrap must reach host construction.");
            Assert.AreEqual(1, wrappedBuilder.BuildCalls);
            Assert.AreEqual(1, host.DisposeCalls,
                "The selected runner owns the host, not an additional using in StartEngineCore.");
            Assert.AreEqual(stdio ? 0 : 1, host.StartCalls);
            Assert.AreEqual(!stdio && completion == "success" ? 1 : 0, host.StopCalls);
            Assert.AreEqual(stdio ? 1 : 0, server.RunCalls);
            Assert.AreEqual(!stdio && completion == "success", lifetime.ApplicationStarted.IsCancellationRequested);
            Assert.AreEqual(completion != "success", telemetry.HasStartupFailed);
            Assert.AreEqual(throughLaunchSeam && completion != "success" ? 1 : 0, failuresObserved);
            Assert.IsFalse(telemetry.IsEnabled);
            Assert.AreEqual(1, exporter.Events.Count(record => record.Name == "dab/engine.stopped"));
            Assert.AreEqual("dab/engine.stopped", exporter.Events.Last().Name);
            Assert.IsFalse(exporter.Events.Any(record => record.Name == "dab/engine.ready"),
                "Running a fake host or entering the stdio loop must not imply metadata readiness.");
            if (stdio)
            {
                Assert.AreEqual(lifetime.ApplicationStopping, server.CancellationToken);
                Assert.IsTrue(host.LoaderDrainedAtDispose, "The helper must drain the loader BEFORE host disposal.");
                Assert.IsTrue(host.TelemetryStoppedAtDispose, "The helper must stop telemetry BEFORE host disposal.");
                Assert.AreEqual(1, host.StoppedEventsAtDispose);
                Assert.IsTrue(loader.ShutdownResourcesDisposed);
            }

            IHostBuilder WrapHost(IHostBuilder builder)
            {
                configuredHosts++;
                return wrappedBuilder = new(builder, host);
            }
        }

        [DataTestMethod]
        [DataRow(typeof(FileSystemRuntimeConfigLoader), false)]
        [DataRow(typeof(IOptions<HostOptions>), false)]
        [DataRow(typeof(FileSystemRuntimeConfigLoader), true)]
        [DataRow(typeof(IOptions<HostOptions>), true)]
        public void StdioCleanupResolutionFailureDisposesExactlyOnce(Type failingService, bool throughStartEngineCore)
        {
            RecordingExporter exporter = new();
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter,
                enableSyntheticCollection: true, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => throw new AssertFailedException("A fake host must not accept a configuration."), startTimer: false);
            using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem(), isCliLoader: true,
                logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);
            using TestApplicationLifetime lifetime = new();
            TestStdioServer server = new(lifetime, "success");
            InvalidOperationException cleanupFailure = new("Synthetic stdio cleanup service-resolution failure.");
            int cleanupResolutionCalls = 0;
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton<IHostApplicationLifetime>(lifetime)
                .AddSingleton<IMcpStdioServer>(server)
                .AddSingleton(loader)
                .AddSingleton(telemetry)
                // Override only the requested cleanup service. The factory, not a constructor
                // or the stdio loop, fails when the helper enters its finally block.
                .AddSingleton(failingService, _ =>
                {
                    cleanupResolutionCalls++;
                    throw cleanupFailure;
                })
                .BuildServiceProvider();
            TestHost host = new(services, lifetime, "success", loader, telemetry, exporter);
            Assert.AreEqual(0, cleanupResolutionCalls, "Fixture construction must not trigger the failure.");

            if (throughStartEngineCore)
            {
                TestHostBuilder? wrappedBuilder = null;
                bool result = Program.StartEngineCore(["--mcp-stdio", "role:anonymous", "--log-level", "Error"],
                    runMcpStdio: true, mcpRole: "anonymous", telemetry, validateUrls: false,
                    configureHost: builder => wrappedBuilder = new(builder, host));

                Assert.IsFalse(result, "StartEngineCore must translate the cleanup exception into a failed launch.");
                Assert.IsNotNull(wrappedBuilder, "The real bootstrap must reach host construction.");
                Assert.AreEqual(1, wrappedBuilder.BuildCalls);
                StringAssert.Contains(_settings.ErrorOutput, "Unable to launch the runtime due to:");
                StringAssert.Contains(_settings.ErrorOutput, cleanupFailure.ToString(),
                    "The bootstrap must report the actual cleanup exception, not a fixture-construction failure.");
                Assert.IsTrue(telemetry.HasStartupFailed);
                Assert.IsFalse(telemetry.IsEnabled);
                Assert.AreEqual(1, exporter.Events.Count(record => record.Name == "dab/engine.startup_failed"));
                Assert.AreEqual(1, exporter.Events.Count(record => record.Name == "dab/engine.stopped"));
                Assert.AreEqual("dab/engine.stopped", exporter.Events.Last().Name);
            }
            else
            {
                InvalidOperationException actual = Assert.ThrowsException<InvalidOperationException>(
                    () => McpStdioHelper.RunMcpStdioHost(host));

                Assert.AreSame(cleanupFailure, actual,
                    "A cleanup exception must escape the direct helper unchanged, after host disposal.");
            }

            Assert.AreEqual(1, server.RunCalls, "The successful stdio loop must finish before cleanup fails.");
            Assert.AreEqual(lifetime.ApplicationStopping, server.CancellationToken);
            Assert.AreEqual(1, cleanupResolutionCalls);
            Assert.AreEqual(1, host.DisposeCalls,
                "The helper must dispose even when cleanup throws; StartEngineCore must not dispose again.");
            Assert.AreEqual(0, host.StartCalls);
            Assert.AreEqual(0, host.StopCalls);
            Assert.IsFalse(loader.ShutdownResourcesDisposed, "The failure must occur before the loader drain.");
            Assert.IsFalse(host.LoaderDrainedAtDispose);
            Assert.IsTrue(host.TelemetryStoppedAtDispose,
                "A failed loader drain must not skip the telemetry drain or host disposal.");
            Assert.AreEqual(1, host.StoppedEventsAtDispose);
            Assert.IsTrue(telemetry.HasStartupFailed);
            EngineTelemetryEvent failure = exporter.Events.Single(record => record.Name == "dab/engine.startup_failed");
            Assert.AreEqual("serving", failure.Properties["failure_stage"]);
            Assert.IsTrue(failure.Sequence < exporter.Events.Single(record => record.Name == "dab/engine.stopped").Sequence);
            Assert.IsFalse(exporter.Events.Any(record => record.Name == "dab/engine.ready"));
        }

        [TestMethod]
        public void ImmutableBootstrapDefaultPreservesPublicAndCliMethodGroups()
        {
            EngineLaunchDependencies defaults = EngineLaunchDependencies.Default;
            EngineLaunchDependencies invocation = defaults with
            {
                SessionFactory = (_, _, _) => EngineTelemetrySession.Create(),
                ConfigureHost = builder => builder
            };
            Func<bool, ProductTelemetryLaunchContext?, string?, EngineTelemetrySession> realFactory = EngineTelemetryHosting.CreateStandalone;
            Assert.AreNotSame(defaults, invocation);
            Assert.AreSame(defaults, EngineLaunchDependencies.Default);
            Assert.AreEqual(realFactory, defaults.SessionFactory);
            Assert.IsNull(defaults.ConfigureHost);

            // These assignments are compile-time guards for the original entry-point signatures.
            // Do not invoke the default bootstrap, which can read the real telemetry environment.
            Func<string[], bool> publicLaunch = Program.StartEngine;
            Func<string[], bool, string?, bool> publicModeLaunch = Program.StartEngine;
            Func<string[], ProductTelemetryLaunchContext?, Action?, bool> cliLaunch = Program.StartEngine;
            Assert.IsTrue(publicLaunch.Method.IsPublic);
            Assert.IsTrue(publicModeLaunch.Method.IsPublic);
            Assert.IsTrue(cliLaunch.Method.IsAssembly);
        }

        private static Task CompleteRun(string completion, TestApplicationLifetime lifetime)
        {
            if (completion == "failure")
            {
                return Task.FromException(new InvalidOperationException("Synthetic host failure."));
            }

            if (completion == "cancellation")
            {
                lifetime.StopApplication();
                return Task.FromCanceled(lifetime.ApplicationStopping);
            }

            return Task.CompletedTask;
        }

        /// <summary>Keep the normal builder, replacing only its final Build result.</summary>
        private sealed class TestHostBuilder(IHostBuilder inner, IHost host) : IHostBuilder
        {
            public IDictionary<object, object> Properties => inner.Properties;
            public int BuildCalls { get; private set; }

            public IHost Build()
            {
                BuildCalls++;
                return host;
            }

            public IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate)
            {
                inner.ConfigureAppConfiguration(configureDelegate);
                return this;
            }

            public IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate)
            {
                inner.ConfigureContainer(configureDelegate);
                return this;
            }

            public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
            {
                inner.ConfigureHostConfiguration(configureDelegate);
                return this;
            }

            public IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate)
            {
                inner.ConfigureServices(configureDelegate);
                return this;
            }

            public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory)
                where TContainerBuilder : notnull
            {
                inner.UseServiceProviderFactory(factory);
                return this;
            }

            public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory)
                where TContainerBuilder : notnull
            {
                inner.UseServiceProviderFactory(factory);
                return this;
            }
        }

        private sealed class TestHost(IServiceProvider services, TestApplicationLifetime lifetime, string completion,
            FileSystemRuntimeConfigLoader loader, EngineTelemetrySession telemetry, RecordingExporter exporter) : IHost
        {
            public IServiceProvider Services => services;
            public int StartCalls { get; private set; }
            public int StopCalls { get; private set; }
            public int DisposeCalls { get; private set; }
            public bool LoaderDrainedAtDispose { get; private set; }
            public bool TelemetryStoppedAtDispose { get; private set; }
            public int StoppedEventsAtDispose { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken = default)
            {
                StartCalls++;
                if (completion == "success")
                {
                    lifetime.MarkStarted();
                    lifetime.StopApplication();
                }

                return CompleteRun(completion, lifetime);
            }

            public Task StopAsync(CancellationToken cancellationToken = default)
            {
                StopCalls++;
                lifetime.MarkStopped();
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                DisposeCalls++;
                LoaderDrainedAtDispose = loader.ShutdownResourcesDisposed;
                TelemetryStoppedAtDispose = !telemetry.IsEnabled;
                StoppedEventsAtDispose = exporter.Events.Count(record => record.Name == "dab/engine.stopped");
                // Fixture-owned services stay available for assertions; no using owns this host.
            }
        }

        private sealed class TestApplicationLifetime : IHostApplicationLifetime, IDisposable
        {
            private readonly CancellationTokenSource _started = new();
            private readonly CancellationTokenSource _stopping = new();
            private readonly CancellationTokenSource _stopped = new();
            public CancellationToken ApplicationStarted => _started.Token;
            public CancellationToken ApplicationStopping => _stopping.Token;
            public CancellationToken ApplicationStopped => _stopped.Token;
            public void MarkStarted() => _started.Cancel();
            public void StopApplication() => _stopping.Cancel();
            public void MarkStopped() => _stopped.Cancel();

            public void Dispose()
            {
                _started.Dispose();
                _stopping.Dispose();
                _stopped.Dispose();
            }
        }

        private sealed class TestStdioServer(TestApplicationLifetime lifetime, string completion) : IMcpStdioServer
        {
            public int RunCalls { get; private set; }
            public CancellationToken CancellationToken { get; private set; }

            public Task RunAsync(CancellationToken cancellationToken)
            {
                RunCalls++;
                CancellationToken = cancellationToken;
                return CompleteRun(completion, lifetime);
            }
        }

        private sealed class RecordingExporter : IEngineTelemetryExporter
        {
            public ConcurrentQueue<EngineTelemetryEvent> Events { get; } = new();

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                Events.Enqueue(record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }

        private sealed class HostSettingsScope : IDisposable
        {
            private readonly ApplicationInsightsOptions _appInsights = Startup.AppInsightsOptions;
            private readonly OpenTelemetryOptions _openTelemetry = Startup.OpenTelemetryOptions;
            private readonly AzureLogAnalyticsOptions _logAnalytics = Startup.AzureLogAnalyticsOptions;
            private readonly FileSinkOptions _fileSink = Startup.FileSinkOptions;
            private readonly DynamicLogLevelProvider _logLevelProvider = Program.LogLevelProvider;
            private readonly LogLevel _minimumLogLevel = Startup.MinimumLogLevel;
            private readonly bool _cliOverriding = Startup.IsCliOverriding;
            private readonly bool _httpsDisabled = Program.IsHttpsRedirectionDisabled;
            private readonly TextWriter _out = Console.Out;
            private readonly TextWriter _error = Console.Error;
            private readonly StringWriter _captureOut = new();
            private readonly StringWriter _captureError = new();

            public string ErrorOutput => _captureError.ToString();

            public HostSettingsScope()
            {
                Startup.AppInsightsOptions = new();
                Startup.OpenTelemetryOptions = new();
                Startup.AzureLogAnalyticsOptions = new();
                Startup.FileSinkOptions = new();
                Program.LogLevelProvider = new();
                Console.SetOut(_captureOut);
                Console.SetError(_captureError);
            }

            public void Dispose()
            {
                Startup.AppInsightsOptions = _appInsights;
                Startup.OpenTelemetryOptions = _openTelemetry;
                Startup.AzureLogAnalyticsOptions = _logAnalytics;
                Startup.FileSinkOptions = _fileSink;
                Program.LogLevelProvider = _logLevelProvider;
                Startup.MinimumLogLevel = _minimumLogLevel;
                Startup.IsCliOverriding = _cliOverriding;
                typeof(Program).GetProperty(nameof(Program.IsHttpsRedirectionDisabled))!.SetValue(null, _httpsDisabled);
                Console.SetOut(_out);
                Console.SetError(_error);
                _captureOut.Dispose();
                _captureError.Dispose();
            }
        }
    }
}

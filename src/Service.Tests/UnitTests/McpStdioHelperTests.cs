// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Telemetry;
using Azure.DataApiBuilder.Service.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpStdioHelperTests
    {
        [TestMethod]
        public void RunMcpStdioHost_DoesNotStartWebHost()
        {
            TestMcpStdioServer stdioServer = new();
            TestMetadataProviderFactory metadataProviderFactory = new();
            using ServiceProvider serviceProvider =
                BuildServices(stdioServer, metadataProviderFactory, out TestApplicationLifetime lifetime);
            TestMcpToolRegistryRefreshService refreshService =
                (TestMcpToolRegistryRefreshService)serviceProvider.GetRequiredService<IMcpToolRegistryRefreshService>();
            TestHost host = new(serviceProvider);

            bool result = McpStdioHelper.RunMcpStdioHost(host);

            Assert.IsTrue(result);
            Assert.AreEqual(0, host.StartAsyncCallCount,
                "MCP stdio mode should not start the ASP.NET Core web host because that binds HTTP ports.");
            Assert.AreEqual(0, host.StopAsyncCallCount,
                "MCP stdio mode should not stop a host that was never started.");
            Assert.AreEqual(1, stdioServer.RunAsyncCallCount,
                "MCP stdio mode should still run the stdio JSON-RPC loop.");
            Assert.AreEqual(0, refreshService.EnsureInitializedCallCount,
                "MCP stdio mode should defer shared tool registry initialization until the protocol loop needs tools.");
            CollectionAssert.AreEqual(
                Array.Empty<string>(),
                metadataProviderFactory.InitializationOrder,
                "MCP stdio mode should not infer metadata before the protocol loop starts.");
            Assert.AreEqual(lifetime.ApplicationStopping, stdioServer.CancellationToken,
                "The stdio loop should keep using the host lifetime cancellation token.");
            Assert.AreEqual(1, host.DisposeCallCount,
                "MCP stdio mode should dispose the host after the stdio loop exits.");
            Assert.AreEqual(0, metadataProviderFactory.InitializeAsyncCallCount,
                "MCP stdio mode must not initialize metadata before the protocol loop needs tools.");
            Assert.IsTrue(serviceProvider.GetRequiredService<FileSystemRuntimeConfigLoader>().ShutdownResourcesDisposed,
                "MCP stdio shutdown must drain the loader before disposing the host.");
        }

        /// <summary>
        /// Startup and loop failures are reported through the bool contract and stderr without
        /// corrupting stdout. A startup failure must not let the server serve uninitialized tools.
        /// </summary>
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RunMcpStdioHost_Fails_ReportsOnStandardErrorAndDisposesHost(bool failDuringStdio)
        {
            Exception failure = failDuringStdio
                ? new InvalidOperationException("The stdio loop failed.")
                : InferenceFailure();
            TestMcpStdioServer stdioServer = new()
            {
                RunAsyncException = failDuringStdio ? failure : null
            };
            TestMetadataProviderFactory metadataProviderFactory = new()
            {
                InitializeAsyncException = failDuringStdio ? null : failure
            };
            using ServiceProvider serviceProvider =
                BuildServices(stdioServer, metadataProviderFactory, out _);
            TestHost host = new(serviceProvider);

            TextWriter originalError = Console.Error;
            TextWriter originalOut = Console.Out;
            using StringWriter capturedError = new();
            using StringWriter capturedOut = new();
            bool result;

            try
            {
                Console.SetError(capturedError);
                Console.SetOut(capturedOut);
                result = McpStdioHelper.RunMcpStdioHost(host);
            }
            finally
            {
                Console.SetError(originalError);
                Console.SetOut(originalOut);
            }

            string reported = capturedError.ToString();

            Assert.AreEqual(!failDuringStdio, result,
                "Only failures from the stdio loop should be reported by the host helper before lazy tool initialization.");
            Assert.AreEqual(1, stdioServer.RunAsyncCallCount,
                "The stdio loop must run even when metadata initialization would fail later.");
            TestMcpToolRegistryRefreshService refreshService =
                (TestMcpToolRegistryRefreshService)serviceProvider.GetRequiredService<IMcpToolRegistryRefreshService>();
            Assert.AreEqual(0, refreshService.EnsureInitializedCallCount,
                "The host helper must not publish tools before the stdio loop needs them.");
            Assert.AreEqual(1, host.DisposeCallCount,
                "The host must still be disposed when initialization or the loop fails.");
            Assert.IsTrue(serviceProvider.GetRequiredService<FileSystemRuntimeConfigLoader>().ShutdownResourcesDisposed,
                "Failure reporting must not bypass the loader's shutdown drain.");
            if (failDuringStdio)
            {
                StringAssert.Contains(reported, "MCP stdio host",
                    "The operator needs to know which host failed, not only that one did.");
                StringAssert.Contains(reported, failure.Message,
                    "GetAwaiter().GetResult() rethrows the original exception, so the cause must survive.");
            }
            else
            {
                Assert.AreEqual(string.Empty, reported,
                    "Lazy metadata failures must not be reported before a tool request starts initialization.");
            }

            Assert.AreEqual(string.Empty, capturedOut.ToString(),
                "stdout is the JSON-RPC channel; a stray byte on it corrupts the protocol.");
        }

        /// <summary>
        /// --mcp-stdio defaults to LogLevel.None, at which Program points stderr at TextWriter.Null.
        /// Reporting without handling that writes into a null sink, which is why the existing
        /// "Unable to launch the runtime" message is never seen in that mode. The report goes to the
        /// real stream without installing a writer that would outlive the call.
        /// </summary>
        [TestMethod]
        public void RunMcpStdioHost_LoopFails_WhenStandardErrorSuppressed_LeavesConsoleUnchanged()
        {
            Exception failure = InferenceFailure();
            TestMcpStdioServer stdioServer = new()
            {
                RunAsyncException = failure
            };
            TestMetadataProviderFactory metadataProviderFactory = new()
            {
                InitializeAsyncException = InferenceFailure()
            };
            using ServiceProvider serviceProvider =
                BuildServices(stdioServer, metadataProviderFactory, out _);
            TestHost host = new(serviceProvider);

            TextWriter originalError = Console.Error;
            bool result;
            bool consoleErrorUntouched;

            try
            {
                // Reproduces Program's LogLevel.None branch for --mcp-stdio.
                Console.SetError(TextWriter.Null);
                result = McpStdioHelper.RunMcpStdioHost(host);
                consoleErrorUntouched = ReferenceEquals(Console.Error, TextWriter.Null);
            }
            finally
            {
                Console.SetError(originalError);
            }

            Assert.IsFalse(result, "The bool contract holds whether or not stderr was suppressed.");
            Assert.IsTrue(consoleErrorUntouched,
                "The report must not leave a replacement writer installed on Console.Error.");
            Assert.AreEqual(1, stdioServer.RunAsyncCallCount,
                "The stdio loop failure should be reported without changing Console.Error.");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RunMcpStdioHost_Canceled_PropagatesCancellationAndDrainsLoader(bool cancelDuringStdio)
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            OperationCanceledException failure = new(cancellation.Token);
            TestMcpStdioServer stdioServer = new()
            {
                RunAsyncException = cancelDuringStdio ? failure : null
            };
            TestMetadataProviderFactory metadataProviderFactory = new()
            {
                InitializeAsyncException = cancelDuringStdio ? null : failure
            };
            using ServiceProvider serviceProvider =
                BuildServices(stdioServer, metadataProviderFactory, out _);
            TestHost host = new(serviceProvider);

            if (cancelDuringStdio)
            {
                OperationCanceledException actual = Assert.ThrowsException<OperationCanceledException>(
                    () => McpStdioHelper.RunMcpStdioHost(host));

                Assert.AreSame(failure, actual, "Cancellation must propagate to Program.StartEngine, not become a startup failure.");
            }
            else
            {
                Assert.IsTrue(McpStdioHelper.RunMcpStdioHost(host),
                    "Metadata cancellation should be deferred until a tool request starts lazy initialization.");
            }

            Assert.AreEqual(1, stdioServer.RunAsyncCallCount);
            TestMcpToolRegistryRefreshService refreshService =
                (TestMcpToolRegistryRefreshService)serviceProvider.GetRequiredService<IMcpToolRegistryRefreshService>();
            Assert.AreEqual(0, refreshService.EnsureInitializedCallCount);
            Assert.AreEqual(1, host.DisposeCallCount);
            Assert.IsTrue(serviceProvider.GetRequiredService<FileSystemRuntimeConfigLoader>().ShutdownResourcesDisposed,
                "Cancellation must still drain the loader before disposing the host.");
        }

        private static DataApiBuilderException InferenceFailure() => new(
            message: "Database object for entity 'Book' has not been inferred.",
            statusCode: HttpStatusCode.ServiceUnavailable,
            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);

        [DataTestMethod]
        [TestCategory("EngineTelemetry")]
        [TestCategory("CliTelemetry")]
        [DoNotParallelize]
        [DataRow(false)]
        [DataRow(true)]
        public void StdioCancellationRecordsStartupFailureOnlyBeforeReadiness(bool ready)
        {
            CapturingProductExporter exporter = new();
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(
                () => exporter, enableSyntheticCollection: true, readEnvironmentVariable: _ => null,
                showNotice: () => { }, resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            if (ready)
            {
                telemetry.AcceptConfiguration(new RuntimeConfig(null, new(DatabaseType.MSSQL, ""), new(new Dictionary<string, Entity>())));
                telemetry.MarkHostReady();
                Assert.IsTrue(telemetry.IsReady);
            }

            OperationCanceledException cancellation = new();
            TestMcpStdioServer stdio = new() { RunAsyncException = cancellation };
            TestMetadataProviderFactory metadata = new() { InitializeAsyncException = new TaskCanceledException() };
            using ServiceProvider services = BuildServices(stdio, metadata, out TestApplicationLifetime lifetime, telemetry);
            TestHost host = new(services);

            OperationCanceledException actual = Assert.ThrowsException<OperationCanceledException>(
                () => McpStdioHelper.RunMcpStdioHost(host));

            Assert.AreSame(cancellation, actual, "Cancellation must still propagate to the existing bootstrap handler.");
            Assert.AreEqual(1, stdio.RunAsyncCallCount);
            Assert.AreEqual(lifetime.ApplicationStopping, stdio.CancellationToken);
            Assert.AreEqual(0, metadata.InitializeAsyncCallCount,
                "Neither pre-ready nor post-ready loop cancellation may eagerly initialize metadata.");
            Assert.AreEqual(1, host.DisposeCallCount);
            Assert.AreEqual(0, host.StartAsyncCallCount);
            Assert.AreEqual(0, host.StopAsyncCallCount);
            TestMcpToolRegistryRefreshService refreshService =
                (TestMcpToolRegistryRefreshService)services.GetRequiredService<IMcpToolRegistryRefreshService>();
            Assert.AreEqual(0, refreshService.EnsureInitializedCallCount);
            CollectionAssert.AreEqual(Array.Empty<string>(), metadata.InitializationOrder);
            Assert.IsTrue(services.GetRequiredService<FileSystemRuntimeConfigLoader>().ShutdownResourcesDisposed,
                "Telemetry cancellation reporting must not bypass the loader's shutdown drain.");
            Assert.AreEqual(ready ? 0 : 1, exporter.Events.Count(record => record.Name == "dab.engine.startup_failed"),
                "The failure must be recorded before the helper stops and disables its session.");
            Assert.AreEqual(ready ? 1 : 0, exporter.Events.Count(record => record.Name == "dab.engine.ready"),
                "Entering the protocol loop must not fabricate readiness.");
            if (!ready)
            {
                EngineTelemetryEvent failure = exporter.Events.Single(record => record.Name == "dab.engine.startup_failed");
                Assert.AreEqual("serving", failure.Properties["failure_stage"]);
                Assert.AreEqual("initialization", failure.Properties["failure_category"]);
                Assert.IsTrue(failure.Sequence < exporter.Events.Single(record => record.Name == "dab.engine.stopped").Sequence);
            }

            Assert.AreEqual("dab.engine.stopped", exporter.Events.Last().Name);
        }

        [DataTestMethod]
        [TestCategory("EngineTelemetry")]
        [TestCategory("CliTelemetry")]
        [DoNotParallelize]
        [DataRow(true)]
        [DataRow(false)]
        public void StdioHostDoesNotReportUnusedMetadataOrRegistryFailuresBeforeToolRequests(bool metadataFails)
        {
            CapturingProductExporter exporter = new();
            int identityResolutions = 0;
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ =>
                {
                    identityResolutions++;
                    return new(Guid.NewGuid(), "ephemeral");
                }, startTimer: false);
            InvalidOperationException initializationFailure = new("synthetic private failure");
            TestMetadataProviderFactory metadata = new() { InitializeAsyncException = metadataFails ? initializationFailure : null };
            TestMcpStdioServer stdio = new();
            using ServiceProvider services = BuildServices(stdio, metadata, out TestApplicationLifetime lifetime, telemetry);
            TestMcpToolRegistryRefreshService refreshService =
                (TestMcpToolRegistryRefreshService)services.GetRequiredService<IMcpToolRegistryRefreshService>();
            refreshService.EnsureInitializedException = initializationFailure;
            TestHost host = new(services);
            TextWriter originalError = Console.Error;
            TextWriter originalOut = Console.Out;
            using StringWriter capturedError = new();
            using StringWriter capturedOut = new();
            try
            {
                Console.SetError(capturedError);
                Console.SetOut(capturedOut);
                Assert.IsTrue(McpStdioHelper.RunMcpStdioHost(host),
                    "Unused initialization failures must not prevent the protocol loop from running normally.");
            }
            finally
            {
                Console.SetError(originalError);
                Console.SetOut(originalOut);
            }

            // The real server's deferred-initialization tests retain metadata/registry order,
            // token propagation, stage classification, protocol-error and privacy assertions.
            // This helper does not own either unused boundary anymore.
            Assert.AreEqual(0, metadata.InitializeAsyncCallCount);
            Assert.AreEqual(0, refreshService.EnsureInitializedCallCount);
            CollectionAssert.AreEqual(Array.Empty<string>(), metadata.InitializationOrder);
            Assert.AreEqual(1, stdio.RunAsyncCallCount);
            Assert.AreEqual(lifetime.ApplicationStopping, stdio.CancellationToken);
            Assert.AreEqual(1, host.DisposeCallCount);
            Assert.AreEqual(0, host.StartAsyncCallCount);
            Assert.AreEqual(0, host.StopAsyncCallCount);
            Assert.IsTrue(services.GetRequiredService<FileSystemRuntimeConfigLoader>().ShutdownResourcesDisposed,
                "A protocol-only session must still drain the loader before disposing the host.");
            Assert.AreEqual(string.Empty, capturedError.ToString());
            Assert.AreEqual(string.Empty, capturedOut.ToString(), "The helper must not write to the JSON-RPC channel.");
            Assert.AreEqual(0, identityResolutions, "The helper must not accept a configuration before tools initialize.");
            Assert.IsFalse(telemetry.HasStartupFailed);
            CollectionAssert.AreEqual(new[] { "dab.engine.process_started", "dab.engine.stopped" },
                exporter.Events.Select(record => record.Name).ToArray());
        }

        [DataTestMethod]
        [TestCategory("EngineTelemetry")]
        [TestCategory("CliTelemetry")]
        [DoNotParallelize]
        [DataRow(false)]
        [DataRow(true)]
        public void StdioHostResolutionOrLoopFailureReportsServingBeforeStopping(bool failDuringResolution)
        {
            CapturingProductExporter exporter = new();
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            InvalidOperationException initializationFailure = new("synthetic private failure");
            TestMcpStdioServer stdio = new() { RunAsyncException = initializationFailure };
            TestMetadataProviderFactory metadata = new() { InitializeAsyncException = initializationFailure };
            using ServiceProvider services = BuildServices(stdio, metadata, out _, telemetry);
            Mock<IServiceProvider> resolvingServices = new(MockBehavior.Strict);
            resolvingServices.Setup(provider => provider.GetService(It.IsAny<Type>())).Returns((Type type) =>
                failDuringResolution && type == typeof(IMcpStdioServer)
                    ? throw initializationFailure
                    : services.GetService(type));
            TestHost host = new(resolvingServices.Object);
            TextWriter originalError = Console.Error;
            TextWriter originalOut = Console.Out;
            using StringWriter capturedError = new();
            using StringWriter capturedOut = new();
            try
            {
                Console.SetError(capturedError);
                Console.SetOut(capturedOut);
                Assert.IsFalse(McpStdioHelper.RunMcpStdioHost(host));
            }
            finally
            {
                Console.SetError(originalError);
                Console.SetOut(originalOut);
            }

            EngineTelemetryEvent failure = exporter.Events.Single(record => record.Name == "dab.engine.startup_failed");
            Assert.AreEqual("serving", failure.Properties["failure_stage"],
                "Server resolution and loop failures must not be mislabeled as unused metadata initialization.");
            Assert.AreEqual("initialization", failure.Properties["failure_category"]);
            Assert.IsTrue(failure.Sequence < exporter.Events.Single(record => record.Name == "dab.engine.stopped").Sequence);
            Assert.AreEqual(0, metadata.InitializeAsyncCallCount);
            Assert.AreEqual(0, ((TestMcpToolRegistryRefreshService)services
                .GetRequiredService<IMcpToolRegistryRefreshService>()).EnsureInitializedCallCount);
            CollectionAssert.AreEqual(Array.Empty<string>(), metadata.InitializationOrder);
            Assert.AreEqual(failDuringResolution ? 0 : 1, stdio.RunAsyncCallCount);
            Assert.AreEqual(1, host.DisposeCallCount);
            Assert.AreEqual(0, host.StartAsyncCallCount);
            Assert.AreEqual(0, host.StopAsyncCallCount);
            Assert.IsTrue(services.GetRequiredService<FileSystemRuntimeConfigLoader>().ShutdownResourcesDisposed);
            StringAssert.Contains(capturedError.ToString(), "MCP stdio host");
            StringAssert.Contains(capturedError.ToString(), initializationFailure.Message);
            Assert.AreEqual(string.Empty, capturedOut.ToString());
            CollectionAssert.AreEqual(new[] { "dab.engine.process_started", "dab.engine.startup_failed", "dab.engine.stopped" },
                exporter.Events.Select(record => record.Name).ToArray());
            Assert.IsFalse(failure.Properties.Values.Any(value => value.Contains("synthetic private failure", StringComparison.Ordinal)));
            Assert.AreEqual("dab.engine.stopped", exporter.Events.Last().Name);
        }

        [TestMethod]
        [TestCategory("EngineTelemetry")]
        [TestCategory("CliTelemetry")]
        [DoNotParallelize]
        public void StdioHostStartsCacheObservationBeforeTheLoopWithoutAcceptingConfiguration()
        {
            CapturingProductExporter exporter = new();
            int identityResolutions = 0;
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ =>
                {
                    identityResolutions++;
                    return new(Guid.NewGuid(), "ephemeral");
                }, startTimer: false);
            TestMetadataProviderFactory metadata = new();
            using ServiceProvider services = BuildServices(new TestMcpStdioServer(), metadata, out TestApplicationLifetime lifetime, telemetry);
            using FusionCache cache = new(Options.Create(new FusionCacheOptions()));
            Mock<IFusionCache> observedCache = new(MockBehavior.Strict);
            List<string> order = new();
            observedCache.SetupGet(value => value.Events).Callback(() => order.Add("cache_observer")).Returns(cache.Events);
            bool enabledInLoop = false;
            bool readyInLoop = true;
            Mock<IMcpStdioServer> stdio = new(MockBehavior.Strict);
            stdio.Setup(server => server.RunAsync(lifetime.ApplicationStopping)).Returns(() =>
            {
                order.Add("loop");
                enabledInLoop = telemetry.IsEnabled;
                readyInLoop = telemetry.IsReady;
                return Task.CompletedTask;
            });
            Mock<IServiceProvider> hostServices = new(MockBehavior.Strict);
            using EngineTelemetryHosting hosting = new(telemetry, hostServices.Object, lifetime);
            hostServices.Setup(provider => provider.GetService(It.IsAny<Type>())).Returns((Type type) =>
            {
                if (type == typeof(EngineTelemetryHosting))
                {
                    return hosting;
                }

                if (type == typeof(IFusionCache))
                {
                    return observedCache.Object;
                }

                return type == typeof(IMcpStdioServer) ? stdio.Object : services.GetService(type);
            });
            TestHost host = new(hostServices.Object);

            Assert.IsTrue(McpStdioHelper.RunMcpStdioHost(host));

            CollectionAssert.AreEqual(new[] { "cache_observer", "loop" }, order,
                "Stdio must start the real hosting observer before entering the loop, without starting the web host.");
            observedCache.VerifyGet(value => value.Events, Times.Once);
            observedCache.VerifyNoOtherCalls();
            stdio.Verify(server => server.RunAsync(lifetime.ApplicationStopping), Times.Once);
            Assert.IsTrue(enabledInLoop, "Observer setup must not silently disable telemetry.");
            Assert.IsFalse(readyInLoop, "Cache observation is not a readiness signal.");
            Assert.AreEqual(0, identityResolutions);
            Assert.AreEqual(0, metadata.InitializeAsyncCallCount);
            Assert.AreEqual(0, ((TestMcpToolRegistryRefreshService)services
                .GetRequiredService<IMcpToolRegistryRefreshService>()).EnsureInitializedCallCount);
            Assert.AreEqual(0, host.StartAsyncCallCount);
            Assert.AreEqual(0, host.StopAsyncCallCount);
            Assert.AreEqual(1, host.DisposeCallCount);
            Assert.IsTrue(services.GetRequiredService<FileSystemRuntimeConfigLoader>().ShutdownResourcesDisposed);
            CollectionAssert.AreEqual(new[] { "dab.engine.process_started", "dab.engine.stopped" },
                exporter.Events.Select(record => record.Name).ToArray());
        }

        private static ServiceProvider BuildServices(
            TestMcpStdioServer stdioServer,
            TestMetadataProviderFactory metadataProviderFactory,
            out TestApplicationLifetime lifetime,
            EngineTelemetrySession? telemetry = null)
        {
            lifetime = new TestApplicationLifetime();

            MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>
            {
                [FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME] =
                    new MockFileData(TestHelper.INITIAL_CONFIG)
            });
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, isCliLoader: true);
            RuntimeConfigProvider runtimeConfigProvider = new(configLoader);
            RuntimeConfigValidator runtimeConfigValidator = new(
                runtimeConfigProvider,
                fileSystem,
                NullLogger<RuntimeConfigValidator>.Instance);
            TestMcpToolRegistryRefreshService refreshService = new(metadataProviderFactory.InitializationOrder);

            ServiceCollection services = new();
            services.AddSingleton(configLoader);
            services.AddSingleton(runtimeConfigProvider);
            services.AddSingleton(runtimeConfigValidator);
            services.AddSingleton<IMcpToolRegistryRefreshService>(refreshService);
            services.AddSingleton<IHostApplicationLifetime>(lifetime);
            services.AddSingleton<IMcpStdioServer>(stdioServer);
            services.AddSingleton<IMetadataProviderFactory>(metadataProviderFactory);
            if (telemetry is not null)
            {
                services.AddSingleton(telemetry);
            }

            return services.BuildServiceProvider();
        }

        private sealed class TestMetadataProviderFactory : IMetadataProviderFactory
        {
            public int InitializeAsyncCallCount { get; private set; }

            public List<string> InitializationOrder { get; } = new();

            public CancellationToken CancellationToken { get; private set; }

            /// <summary>
            /// When set, InitializeAsync() returns a faulted task carrying it, standing in for a
            /// metadata inference failure such as an unreachable database or an entity that is
            /// missing from the schema. A faulted task rather than a synchronous throw, because
            /// the real MetadataProviderFactory.InitializeAsync is async.
            /// </summary>
            public Exception? InitializeAsyncException { get; init; }

            public Task InitializeAsync() => InitializeAsync(CancellationToken.None);

            public Task InitializeAsync(CancellationToken cancellationToken)
            {
                InitializeAsyncCallCount++;
                InitializationOrder.Add("metadata");
                CancellationToken = cancellationToken;
                return InitializeAsyncException is null
                    ? Task.CompletedTask
                    : Task.FromException(InitializeAsyncException);
            }

            public void InitializeAsync(
                Dictionary<string, Dictionary<string, DatabaseObject>> entityToDatabaseObjectMap,
                Dictionary<string, Dictionary<string, string>> graphQLStoredProcedureExposedNameToEntityNameMap)
                => InitializeAsyncCallCount++;

            public ISqlMetadataProvider GetMetadataProvider(string dataSourceName)
                => throw new NotImplementedException();

            public IEnumerable<ISqlMetadataProvider> ListMetadataProviders()
                => Array.Empty<ISqlMetadataProvider>();

            public List<Exception> GetAllMetadataExceptions()
                => new();
        }

        private sealed class TestMcpToolRegistryRefreshService : IMcpToolRegistryRefreshService
        {
            private readonly List<string> _initializationOrder;

            public TestMcpToolRegistryRefreshService(List<string> initializationOrder)
            {
                _initializationOrder = initializationOrder;
            }

            public int EnsureInitializedCallCount { get; private set; }

            public CancellationToken CancellationToken { get; private set; }

            public Exception? EnsureInitializedException { get; set; }

            public void EnsureInitialized() => EnsureInitialized(CancellationToken.None);

            public void EnsureInitialized(CancellationToken cancellationToken)
            {
                EnsureInitializedCallCount++;
                CancellationToken = cancellationToken;
                _initializationOrder.Add("registry");
                if (EnsureInitializedException is not null)
                {
                    throw EnsureInitializedException;
                }
            }
        }

        private sealed class TestHost : IHost
        {
            public TestHost(System.IServiceProvider services)
            {
                Services = services;
            }

            public System.IServiceProvider Services { get; }

            public int StartAsyncCallCount { get; private set; }

            public int StopAsyncCallCount { get; private set; }

            public int DisposeCallCount { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken = default)
            {
                StartAsyncCallCount++;
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken = default)
            {
                StopAsyncCallCount++;
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

        private sealed class TestApplicationLifetime : IHostApplicationLifetime
        {
            private readonly CancellationTokenSource _applicationStopping = new();

            public CancellationToken ApplicationStarted => CancellationToken.None;

            public CancellationToken ApplicationStopping => _applicationStopping.Token;

            public CancellationToken ApplicationStopped => CancellationToken.None;

            public void StopApplication()
            {
                _applicationStopping.Cancel();
            }
        }

        private sealed class TestMcpStdioServer : IMcpStdioServer
        {
            public int RunAsyncCallCount { get; private set; }

            public CancellationToken CancellationToken { get; private set; }

            public Exception? RunAsyncException { get; init; }

            public Task RunAsync(CancellationToken cancellationToken)
            {
                RunAsyncCallCount++;
                CancellationToken = cancellationToken;
                return RunAsyncException is null
                    ? Task.CompletedTask
                    : Task.FromException(RunAsyncException);
            }
        }

        private sealed class CapturingProductExporter : IEngineTelemetryExporter
        {
            public ConcurrentQueue<EngineTelemetryEvent> Events { get; } = new();

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                Events.Enqueue(record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }
    }
}

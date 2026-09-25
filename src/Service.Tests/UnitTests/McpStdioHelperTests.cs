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
using Azure.DataApiBuilder.Service.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
            Assert.AreEqual(1, refreshService.EnsureInitializedCallCount,
                "MCP stdio mode should initialize the shared tool registry before running the loop.");
            CollectionAssert.AreEqual(
                new[] { "metadata", "registry" },
                metadataProviderFactory.InitializationOrder,
                "MCP stdio mode should initialize metadata before publishing the registry.");
            Assert.IsTrue(metadataProviderFactory.CancellationToken.CanBeCanceled,
                "Metadata initialization must receive the loader's shutdown cancellation token.");
            Assert.AreEqual(metadataProviderFactory.CancellationToken, refreshService.CancellationToken,
                "Metadata initialization and registry publication must share the serialized operation's token.");
            Assert.AreEqual(lifetime.ApplicationStopping, stdioServer.CancellationToken,
                "The stdio loop should keep using the host lifetime cancellation token.");
            Assert.AreEqual(1, host.DisposeCallCount,
                "MCP stdio mode should dispose the host after the stdio loop exits.");
            Assert.AreEqual(1, metadataProviderFactory.InitializeAsyncCallCount,
                "MCP stdio mode must initialize metadata exactly once through the shared runtime initialization path.");
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

            Assert.IsFalse(result, "A host failure should be reported through the bool contract.");
            Assert.AreEqual(failDuringStdio ? 1 : 0, stdioServer.RunAsyncCallCount,
                "The stdio loop must run only when metadata initialization succeeds.");
            TestMcpToolRegistryRefreshService refreshService =
                (TestMcpToolRegistryRefreshService)serviceProvider.GetRequiredService<IMcpToolRegistryRefreshService>();
            Assert.AreEqual(failDuringStdio ? 1 : 0, refreshService.EnsureInitializedCallCount,
                "The registry must not publish tools after metadata initialization fails.");
            Assert.AreEqual(1, host.DisposeCallCount,
                "The host must still be disposed when initialization or the loop fails.");
            Assert.IsTrue(serviceProvider.GetRequiredService<FileSystemRuntimeConfigLoader>().ShutdownResourcesDisposed,
                "Failure reporting must not bypass the loader's shutdown drain.");
            StringAssert.Contains(reported, "MCP stdio host",
                "The operator needs to know which host failed, not only that one did.");
            StringAssert.Contains(reported, failure.Message,
                "GetAwaiter().GetResult() rethrows the original exception, so the cause must survive.");
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
        public void RunMcpStdioHost_StartupFails_WhenStandardErrorSuppressed_LeavesConsoleUnchanged()
        {
            TestMcpStdioServer stdioServer = new();
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
            Assert.AreEqual(0, stdioServer.RunAsyncCallCount,
                "The stdio loop must not run after startup failed.");
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

            OperationCanceledException actual = Assert.ThrowsException<OperationCanceledException>(
                () => McpStdioHelper.RunMcpStdioHost(host));

            Assert.AreSame(failure, actual, "Cancellation must propagate to Program.StartEngine, not become a startup failure.");
            Assert.AreEqual(cancelDuringStdio ? 1 : 0, stdioServer.RunAsyncCallCount);
            TestMcpToolRegistryRefreshService refreshService =
                (TestMcpToolRegistryRefreshService)serviceProvider.GetRequiredService<IMcpToolRegistryRefreshService>();
            Assert.AreEqual(cancelDuringStdio ? 1 : 0, refreshService.EnsureInitializedCallCount);
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

            TestMcpStdioServer stdio = new() { RunAsyncException = ready ? new OperationCanceledException() : null };
            TestMetadataProviderFactory metadata = new() { InitializeAsyncException = ready ? null : new TaskCanceledException() };
            using ServiceProvider services = BuildServices(stdio, metadata, out _, telemetry);
            TestHost host = new(services);

            try
            {
                McpStdioHelper.RunMcpStdioHost(host);
                Assert.Fail("Cancellation must still propagate to the existing bootstrap handler.");
            }
            catch (OperationCanceledException)
            {
                // Preserve the existing host's cancellation contract.
            }

            Assert.AreEqual(ready ? 1 : 0, stdio.RunAsyncCallCount);
            Assert.AreEqual(1, host.DisposeCallCount);
            Assert.AreEqual(ready ? 0 : 1, exporter.Events.Count(record => record.Name == "dab.engine.startup_failed"),
                "The failure must be recorded before the helper stops and disables its session.");
            if (!ready)
            {
                Assert.AreEqual("metadata", exporter.Events.Single(record => record.Name == "dab.engine.startup_failed").Properties["failure_stage"]);
            }

            Assert.AreEqual("dab.engine.stopped", exporter.Events.Last().Name);
        }

        [DataTestMethod]
        [TestCategory("EngineTelemetry")]
        [DataRow(true, "metadata")]
        [DataRow(false, "serving")]
        public void StdioReportsMetadataAndServingFailuresSeparately(bool metadataFails, string expectedStage)
        {
            CapturingProductExporter exporter = new();
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { }, startTimer: false);
            TestMetadataProviderFactory metadata = new() { InitializeAsyncException = metadataFails ? new InvalidOperationException("synthetic private failure") : null };
            TestMcpStdioServer stdio = new();
            using ServiceProvider services = BuildServices(stdio, metadata, out _, telemetry,
                registryFailure: metadataFails ? null : new InvalidOperationException("synthetic private failure"));
            TextWriter originalError = Console.Error;
            using StringWriter capture = new();
            try
            {
                Console.SetError(capture);
                Assert.IsFalse(McpStdioHelper.RunMcpStdioHost(new TestHost(services)));
            }
            finally
            {
                Console.SetError(originalError);
            }

            EngineTelemetryEvent failure = exporter.Events.Single(record => record.Name == "dab.engine.startup_failed");
            Assert.AreEqual(expectedStage, failure.Properties["failure_stage"]);
            Assert.AreEqual("initialization", failure.Properties["failure_category"]);
            Assert.AreEqual(1, metadata.InitializeAsyncCallCount);
            Assert.AreEqual(0, stdio.RunAsyncCallCount, "Tool registration must fail before the ready stdio loop begins.");
            Assert.IsFalse(exporter.Events.Any(record => record.Name == "dab.engine.ready"));
            Assert.IsFalse(failure.Properties.Values.Any(value => value.Contains("synthetic private failure", StringComparison.Ordinal)));
        }

        [TestMethod]
        [TestCategory("EngineTelemetry")]
        public void DisabledMcpCannotReportReadyBeforeStdioServiceResolution()
        {
            CapturingProductExporter exporter = new();
            using EngineTelemetrySession telemetry = EngineTelemetrySession.Create(() => exporter,
                enableSyntheticCollection: true, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            MockFileSystem files = new(new Dictionary<string, MockFileData>
            {
                [FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME] = new(TestHelper.INITIAL_CONFIG)
            });
            using FileSystemRuntimeConfigLoader loader = new(files, isCliLoader: true);
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = telemetry };
            RuntimeConfig config = provider.GetConfig();
            loader.RuntimeConfig = config with { Runtime = config.Runtime! with { Mcp = new(Enabled: false) } };
            Assert.IsTrue(loader.RuntimeConfig.IsRestEnabled);
            Assert.IsFalse(loader.RuntimeConfig.IsMcpEnabled);
            TestMetadataProviderFactory metadata = new();
            ServiceCollection registrations = new();
            registrations.AddSingleton(telemetry);
            registrations.AddSingleton(loader);
            registrations.AddSingleton(provider);
            registrations.AddSingleton(new RuntimeConfigValidator(provider, files, NullLogger<RuntimeConfigValidator>.Instance));
            registrations.AddSingleton<IMetadataProviderFactory>(metadata);
            registrations.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
            registrations.AddDabMcpServer(provider);
            registrations.AddSingleton<IMcpStdioServer, McpStdioServer>();
            using ServiceProvider services = registrations.BuildServiceProvider();
            Assert.IsNull(services.GetService<McpToolRegistry>());
            TestHost host = new(services);
            TextWriter originalError = Console.Error;
            using StringWriter capturedError = new();
            try
            {
                Console.SetError(capturedError);
                Assert.IsFalse(McpStdioHelper.RunMcpStdioHost(host));
            }
            finally
            {
                Console.SetError(originalError);
            }

            Assert.AreEqual(1, metadata.InitializeAsyncCallCount);
            Assert.AreEqual(1, host.DisposeCallCount);
            Assert.IsFalse(exporter.Events.Any(record => record.Name == "dab.engine.ready"));
            EngineTelemetryEvent failure = exporter.Events.Single(record => record.Name == "dab.engine.startup_failed");
            Assert.AreEqual("serving", failure.Properties["failure_stage"]);
            Assert.IsTrue(loader.ShutdownResourcesDisposed);
        }

        private static ServiceProvider BuildServices(
            TestMcpStdioServer stdioServer,
            TestMetadataProviderFactory metadataProviderFactory,
            out TestApplicationLifetime lifetime,
            EngineTelemetrySession? telemetry = null,
            Exception? registryFailure = null)
        {
            lifetime = new TestApplicationLifetime();

            MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>
            {
                [FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME] =
                    new MockFileData(TestHelper.INITIAL_CONFIG)
            });
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, isCliLoader: true);
            RuntimeConfigProvider runtimeConfigProvider = new(configLoader) { ProductTelemetry = telemetry };
            RuntimeConfigValidator runtimeConfigValidator = new(
                runtimeConfigProvider,
                fileSystem,
                NullLogger<RuntimeConfigValidator>.Instance);
            TestMcpToolRegistryRefreshService refreshService = new(metadataProviderFactory.InitializationOrder)
            {
                EnsureInitializedException = registryFailure
            };

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

            public Exception? EnsureInitializedException { get; init; }

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

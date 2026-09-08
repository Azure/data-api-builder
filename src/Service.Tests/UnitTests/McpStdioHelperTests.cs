// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            TestHost host = new(serviceProvider);

            bool result = McpStdioHelper.RunMcpStdioHost(host);

            Assert.IsTrue(result);
            Assert.AreEqual(0, host.StartAsyncCallCount,
                "MCP stdio mode should not start the ASP.NET Core web host because that binds HTTP ports.");
            Assert.AreEqual(0, host.StopAsyncCallCount,
                "MCP stdio mode should not stop a host that was never started.");
            Assert.AreEqual(1, stdioServer.RunAsyncCallCount,
                "MCP stdio mode should still run the stdio JSON-RPC loop.");
            Assert.AreEqual(lifetime.ApplicationStopping, stdioServer.CancellationToken,
                "The stdio loop should keep using the host lifetime cancellation token.");
            Assert.AreEqual(1, host.DisposeCallCount,
                "MCP stdio mode should dispose the host after the stdio loop exits.");
            Assert.AreEqual(1, metadataProviderFactory.InitializeAsyncCallCount,
                "MCP stdio mode must initialize the metadata providers itself: it never calls " +
                "host.Run(), so Startup.Configure -- the only caller of PerformOnConfigChangeAsync " +
                "-- never runs, and without this every tool call fails with " +
                "\"Database object for entity '<name>' has not been inferred.\"");
        }

        /// <summary>
        /// A startup failure must not escape RunMcpStdioHost, whose contract is a bool, and must stop
        /// the server rather than let it serve entities that have no database object -- the failure
        /// this initialization exists to prevent. stdout carries JSON-RPC, so it reports on stderr.
        /// </summary>
        [TestMethod]
        public void RunMcpStdioHost_StartupFails_ReportsOnStandardErrorAndDoesNotServeTools()
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

            Assert.IsFalse(result, "A startup failure should be reported through the bool contract.");
            Assert.AreEqual(0, stdioServer.RunAsyncCallCount,
                "The stdio loop must not run: it would advertise entities that have no database object.");
            Assert.AreEqual(1, host.DisposeCallCount,
                "The host must still be disposed when startup fails.");
            StringAssert.Contains(reported, "MCP stdio host",
                "The operator needs to know which host failed, not only that one did.");
            StringAssert.Contains(reported, "has not been inferred",
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

        private static DataApiBuilderException InferenceFailure() => new(
            message: "Database object for entity 'Book' has not been inferred.",
            statusCode: HttpStatusCode.ServiceUnavailable,
            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);

        private static ServiceProvider BuildServices(
            TestMcpStdioServer stdioServer,
            TestMetadataProviderFactory metadataProviderFactory,
            out TestApplicationLifetime lifetime)
        {
            lifetime = new TestApplicationLifetime();

            ServiceCollection services = new();
            services.AddSingleton<McpToolRegistry>();
            services.AddSingleton<IHostApplicationLifetime>(lifetime);
            services.AddSingleton<IMcpStdioServer>(stdioServer);
            services.AddSingleton<IMetadataProviderFactory>(metadataProviderFactory);

            return services.BuildServiceProvider();
        }

        private sealed class TestMetadataProviderFactory : IMetadataProviderFactory
        {
            public int InitializeAsyncCallCount { get; private set; }

            /// <summary>
            /// When set, InitializeAsync() returns a faulted task carrying it, standing in for a
            /// metadata inference failure such as an unreachable database or an entity that is
            /// missing from the schema. A faulted task rather than a synchronous throw, because
            /// the real MetadataProviderFactory.InitializeAsync is async.
            /// </summary>
            public Exception? InitializeAsyncException { get; init; }

            public Task InitializeAsync()
            {
                InitializeAsyncCallCount++;
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

            public Task RunAsync(CancellationToken cancellationToken)
            {
                RunAsyncCallCount++;
                CancellationToken = cancellationToken;
                return Task.CompletedTask;
            }
        }
    }
}

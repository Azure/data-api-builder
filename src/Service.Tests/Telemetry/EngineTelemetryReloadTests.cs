// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// File reloads enter the real watcher callback, parser and synchronous dispatcher, but do
    /// not install RuntimeConfigProvider's disk/schema validation or real metadata subscribers.
    /// Late configuration uses real Startup and RuntimeConfigProvider.Initialize in TestServer;
    /// successful metadata initialization is real, with empty entities and a strict executor.
    /// These are not OS watcher, database-serving or HTTP /configuration endpoint tests.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [DoNotParallelize]
    public class EngineTelemetryReloadTests
    {
        private const string SENTINEL = "RELOAD_PRIVATE_FAILURE_62dfea";
        private const string CONNECTION_STRING = "Server=engine-telemetry.invalid;Database=unused;Integrated Security=true;Connect Timeout=1;";
        private const string PROCESS_STARTED = "dab.engine.process_started";
        private const string READY = "dab.engine.ready";
        private const string CHANGED = "dab.engine.configuration_changed";
        private const string REJECTED = "dab.engine.configuration_change_failed";
        private const string STOPPED = "dab.engine.stopped";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
        private static readonly Guid _apiId = new("d6915f03-fc48-4ab8-8a09-906d9a2d7b46");
        private static readonly string[] _reloadEvents =
        [
            QUERY_MANAGER_FACTORY_ON_CONFIG_CHANGED,
            METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED,
            QUERY_ENGINE_FACTORY_ON_CONFIG_CHANGED,
            MUTATION_ENGINE_FACTORY_ON_CONFIG_CHANGED,
            DOCUMENTOR_ON_CONFIG_CHANGED,
            AUTHZ_RESOLVER_ON_CONFIG_CHANGED,
            GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED,
            GRAPHQL_SCHEMA_CREATOR_ON_CONFIG_CHANGED,
            GRAPHQL_SCHEMA_REFRESH_ON_CONFIG_CHANGED,
            LOG_LEVEL_INITIALIZER_ON_CONFIG_CHANGE
        ];

        [DataTestMethod]
        [DataRow("validation", "validation")]
        [DataRow("metadata", "metadata")]
        [DataRow("serving", "serving")]
        public async Task InitialWebStartupReportsTheActualFailureStage(string boundary, string expectedStage)
        {
            string directory = Path.Combine(Path.GetTempPath(), "dab-telemetry-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "dab-config.json");
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter);
            Mock<IMetadataProviderFactory> metadata = new(MockBehavior.Strict);
            metadata.Setup(factory => factory.InitializeAsync()).Returns(boundary == "metadata"
                ? Task.FromException(new InvalidOperationException(SENTINEL))
                : Task.CompletedTask);
            IHost? host = null;
            try
            {
                string json = CreateConfigJson();
                if (boundary == "validation")
                {
                    json = json.Replace("\"path\": \"/api\"", "\"path\": \"invalid path\"", StringComparison.Ordinal);
                }

                await File.WriteAllTextAsync(path, json);
                host = new HostBuilder()
                    .UseEnvironment(Environments.Production)
                    .ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConfigFileName"] = path,
                        ["CONNSTRING"] = CONNECTION_STRING
                    }))
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureWebHost(web => web
                        .UseSetting(WebHostDefaults.ApplicationKey, typeof(Startup).Assembly.GetName().Name)
                        .UseTestServer()
                        .UseStartup(context => new Startup(context.Configuration, NullLogger<Startup>.Instance) { ProductTelemetry = session })
                        .ConfigureTestServices(services =>
                        {
                            foreach (ServiceDescriptor descriptor in services.Where(descriptor =>
                                descriptor.ServiceType.IsConstructedGenericType &&
                                descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ILogger<>)).ToArray())
                            {
                                services.Remove(descriptor);
                            }

                            services.Replace(ServiceDescriptor.Singleton(new DynamicLogLevelProvider()));
                            services.Replace(ServiceDescriptor.Singleton(metadata.Object));
                            if (boundary == "serving")
                            {
                                services.Replace(ServiceDescriptor.Singleton<GraphQLSchemaCreator>(_ => throw new InvalidOperationException(SENTINEL)));
                            }
                        })).Build();
                using CancellationTokenSource timeout = new(_timeout);
                try
                {
                    await host.StartAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
                {
                    // Real Startup requests host shutdown after its initialization failure.
                }

                metadata.Verify(factory => factory.InitializeAsync(), boundary == "validation" ? Times.Never() : Times.Once());
                Assert.IsFalse(session.IsReady);
                EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
                EngineTelemetryEvent failure = records.Single(record => record.Name == "dab.engine.startup_failed");
                Assert.AreEqual(expectedStage, failure.Properties["failure_stage"]);
                Assert.AreEqual("initialization", failure.Properties["failure_category"]);
                Assert.IsFalse(records.Any(record => record.Name == READY || record.Name == CHANGED));
            }
            finally
            {
                host?.Dispose();
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public async Task FileReloadAcceptsReplacementOnlyAfterEverySynchronousSubscriberReturns()
        {
            using ReloadFixture fixture = new();

            fixture.Reload(CreateConfigJson(graphQl: true));

            CollectionAssert.AreEqual(
                new[] { "change_token" }.Concat(_reloadEvents).Append("accepted").ToArray(),
                fixture.Trace);
            Assert.AreEqual(_reloadEvents.Length + 1, fixture.SubscriberConfigurations.Count);
            Assert.IsTrue(fixture.SubscriberConfigurations.All(observed =>
                ReferenceEquals(fixture.InitialConfig, observed.Config) && observed.Epoch == 1),
                "Telemetry must retain the old epoch throughout the synchronous reload dispatch.");
            (RuntimeConfig? replacement, bool accepted) = fixture.Completions.Single();
            Assert.IsTrue(accepted);
            Assert.IsNotNull(replacement);
            Assert.AreSame(fixture.Loader.RuntimeConfig, replacement);
            Assert.AreNotSame(fixture.InitialConfig, replacement);
            Assert.IsTrue(replacement.IsDevelopmentMode());
            Assert.IsTrue(replacement.IsRestEnabled);
            Assert.IsTrue(replacement.IsGraphQLEnabled);
            AssertConfiguration(fixture.Session, replacement, epoch: 2);

            EngineTelemetryEvent[] records = await DrainAsync(fixture.Session, fixture.Exporter);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, CHANGED, STOPPED }, records.Select(record => record.Name).ToArray());
            Assert.AreEqual("disabled", records.Single(record => record.Name == READY).Properties["runtime.graphql.effective"]);
            EngineTelemetryEvent changed = records.Single(record => record.Name == CHANGED);
            Assert.AreEqual(2L, changed.ConfigurationEpoch);
            Assert.AreEqual("hot_reload", changed.Properties["configuration_delivery"]);
            Assert.AreEqual("enabled", changed.Properties["runtime.graphql.effective"]);
        }

        [DataTestMethod]
        [DataRow("parse")]
        [DataRow("validation")]
        [DataRow(METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED)]
        [DataRow(LOG_LEVEL_INITIALIZER_ON_CONFIG_CHANGE)]
        public async Task FileReloadRejectsParseOrSubscriberFailureWithoutAdvancingTelemetryAndCanRecover(string failurePoint)
        {
            using ReloadFixture fixture = new();
            bool reject = true;
            using IDisposable? validation = failurePoint == "validation"
                ? ChangeToken.OnChange(fixture.Loader.GetChangeToken, () =>
                {
                    if (reject)
                    {
                        throw new InvalidOperationException(SENTINEL);
                    }
                }) : null;
            if (failurePoint is not ("parse" or "validation"))
            {
                fixture.Handler.Subscribe(failurePoint, (_, _) =>
                {
                    if (reject)
                    {
                        throw new InvalidOperationException(SENTINEL);
                    }
                });
            }

            fixture.Reload(failurePoint == "parse" ? "{" : CreateConfigJson(graphQl: true));

            (RuntimeConfig? rejectedConfig, bool accepted) = fixture.Completions.Single();
            Assert.IsFalse(accepted);
            Assert.IsNull(rejectedConfig, "A rejected candidate must never be supplied to the telemetry observer.");
            AssertConfiguration(fixture.Session, fixture.InitialConfig, epoch: 1);
            Assert.IsTrue(fixture.Session.IsReady);
            if (failurePoint == "parse")
            {
                CollectionAssert.AreEqual(new[] { "rejected" }, fixture.Trace);
                Assert.IsTrue(fixture.Loader.IsParseErrorEmitted);
                Assert.AreSame(fixture.InitialConfig, fixture.Loader.RuntimeConfig);
            }
            else if (failurePoint == "validation")
            {
                CollectionAssert.AreEqual(new[] { "change_token", "rejected" }, fixture.Trace);
            }
            else
            {
                CollectionAssert.AreEqual(new[] { "change_token" }
                    .Concat(_reloadEvents.Take(Array.IndexOf(_reloadEvents, failurePoint) + 1))
                    .Append("rejected").ToArray(), fixture.Trace);
                // Without RuntimeConfigProvider there is no validation/rollback subscriber.
                // Assert telemetry retention, not rollback of the loader's parsed candidate.
                Assert.AreNotSame(fixture.InitialConfig, fixture.Loader.RuntimeConfig);
            }

            reject = false;
            fixture.Trace.Clear();
            fixture.Reload(CreateConfigJson(graphQl: true));
            Assert.AreEqual(2, fixture.Completions.Count);
            Assert.IsTrue(fixture.Completions[1].Accepted);
            Assert.IsNotNull(fixture.Loader.RuntimeConfig);
            AssertConfiguration(fixture.Session, fixture.Loader.RuntimeConfig, epoch: 2);

            EngineTelemetryEvent[] records = await DrainAsync(fixture.Session, fixture.Exporter);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, REJECTED, CHANGED, STOPPED }, records.Select(record => record.Name).ToArray());
            EngineTelemetryEvent failure = records.Single(record => record.Name == REJECTED);
            Assert.AreEqual(1L, failure.ConfigurationEpoch);
            Assert.AreEqual("configuration", failure.Properties["failure_category"]);
            Assert.IsTrue(failure.Properties.ContainsKey("failure_stage"));
            Assert.AreEqual(failurePoint == "parse" ? "parsing" :
                failurePoint == "validation" ? "validation" :
                failurePoint == METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED ? "metadata" : "serving",
                failure.Properties["failure_stage"]);
            Assert.IsFalse(failure.Properties.ContainsKey("snapshot_schema"));
            Assert.AreEqual(2L, records.Single(record => record.Name == CHANGED).ConfigurationEpoch);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ThrowingTelemetryObserverDoesNotEscapeOrTurnAcceptanceIntoRejection(bool invalidJson)
        {
            using ReloadFixture fixture = new();
            Action<RuntimeConfig?, bool, TelemetryFailureStage>? forward = fixture.Loader.TelemetryReloadCompleted;
            Assert.IsNotNull(forward);
            fixture.Loader.TelemetryReloadCompleted = (config, accepted, stage) =>
            {
                forward(config, accepted, stage);
                throw new InvalidOperationException(SENTINEL);
            };

            fixture.Reload(invalidJson ? "{" : CreateConfigJson(graphQl: true));

            Assert.AreEqual(1, fixture.Completions.Count, "Observer failure must not cause a second, rejected notification.");
            Assert.AreEqual(!invalidJson, fixture.Completions.Single().Accepted);
            Assert.IsNotNull(fixture.Loader.RuntimeConfig);
            AssertConfiguration(fixture.Session, fixture.Loader.RuntimeConfig, invalidJson ? 1 : 2);
            Assert.IsTrue(fixture.Session.IsReady);
            EngineTelemetryEvent[] records = await DrainAsync(fixture.Session, fixture.Exporter);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, invalidJson ? REJECTED : CHANGED, STOPPED },
                records.Select(record => record.Name).ToArray());
        }

        [DataTestMethod]
        [DataRow(false, "{")]
        [DataRow(true, "{")]
        [DataRow(false, "{\"runtime\":{\"rest\":7}}")]
        [DataRow(true, "{\"runtime\":{\"rest\":7}}")]
        public async Task LateConfigurationParseOrMergeRejectionIsReportedExactlyOnce(bool versionTwo, string configuration)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter);
            Mock<IQueryExecutor> executor = new(MockBehavior.Strict);
            await using LateConfigServer server = CreateLateConfigServer(session, CreateQueryManager(executor).Object);
            RuntimeConfigProvider provider = AssertUnconfiguredHost(server.Server, session);
            ConfigurationController controller = new(provider, NullLogger<ConfigurationController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            ActionResult result = versionTwo
                ? await controller.Index(new ConfigurationPostParametersV2(configuration, "{}", null, null))
                : await controller.Index(new ConfigurationPostParameters(configuration, null, CONNECTION_STRING, null));
            Assert.IsInstanceOfType<BadRequestResult>(result);
            Assert.IsFalse(session.IsReady);
            AssertConfiguration(session, config: null, epoch: 0);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, REJECTED, STOPPED }, records.Select(record => record.Name).ToArray());
            Assert.AreEqual(0L, records.Single(record => record.Name == REJECTED).ConfigurationEpoch);
            Assert.IsTrue(records.Single(record => record.Name == REJECTED).Properties.ContainsKey("failure_stage"));
            Assert.AreEqual("parsing", records.Single(record => record.Name == REJECTED).Properties["failure_stage"]);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task LateConfigurationValidationFailureIsDistinctAndCanRecover(bool versionTwo)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter);
            Mock<IQueryExecutor> executor = new(MockBehavior.Strict);
            await using LateConfigServer server = CreateLateConfigServer(session, CreateQueryManager(executor).Object);
            RuntimeConfigProvider provider = AssertUnconfiguredHost(server.Server, session);
            string invalid = CreateConfigJson().Replace("\"path\": \"/api\"", "\"path\": \"invalid path\"", StringComparison.Ordinal);
            bool accepted = versionTwo
                ? await provider.Initialize(invalid, schema: null, accessToken: null)
                : await provider.Initialize(invalid, graphQLSchema: null, connectionString: CONNECTION_STRING, accessToken: null, replacementSettings: null);
            Assert.IsFalse(accepted);
            Assert.IsFalse(session.IsReady);
            AssertConfiguration(session, config: null, epoch: 0);
            Assert.IsTrue(await InitializeAsync(provider, versionTwo));
            Assert.IsTrue(session.IsReady);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            EngineTelemetryEvent rejection = records.Single(record => record.Name == REJECTED);
            Assert.AreEqual("validation", rejection.Properties["failure_stage"]);
            Assert.AreEqual("configuration", rejection.Properties["failure_category"]);
            Assert.AreEqual(0L, rejection.ConfigurationEpoch);
            Assert.AreEqual(1L, records.Single(record => record.Name == READY).ConfigurationEpoch);
            Assert.IsFalse(records.Any(record => record.Name == "dab.engine.startup_failed"));
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(true, true)]
        public async Task LateConfigurationWaitsForAllHandlersBeforeTelemetryAcceptance(bool versionTwo, bool accepted)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter);
            Mock<IQueryExecutor> executor = new(MockBehavior.Strict);
            await using LateConfigServer server = CreateLateConfigServer(session, CreateQueryManager(executor).Object);
            RuntimeConfigProvider provider = AssertUnconfiguredHost(server.Server, session);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(_timeout);
                return accepted;
            });
            Task<bool> initializing = InitializeAsync(provider, versionTwo);
            try
            {
                await entered.Task.WaitAsync(_timeout);
                Assert.IsFalse(initializing.IsCompleted);
                Assert.IsFalse(session.IsReady, "Startup's handler alone cannot accept configuration while another handler is pending.");
                AssertConfiguration(session, config: null, epoch: 0);
            }
            finally
            {
                release.TrySetResult();
                Assert.AreEqual(accepted, await initializing.WaitAsync(_timeout));
            }

            Assert.AreEqual(accepted, session.IsReady);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, accepted ? READY : REJECTED, STOPPED },
                records.Select(record => record.Name).ToArray());
            Assert.AreEqual(accepted ? 1L : 0L, records[1].ConfigurationEpoch);
            if (!accepted)
            {
                Assert.AreEqual("initialization", records[1].Properties["failure_stage"], "Custom handler rejection has no more specific known boundary.");
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task StartupAcceptsLateConfigurationThroughRealEmptyEntityMetadataInitialization(bool versionTwo)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter);
            Mock<IQueryExecutor> executor = new(MockBehavior.Strict);
            Mock<IAbstractQueryManagerFactory> queryManager = CreateQueryManager(executor);
            await using LateConfigServer server = CreateLateConfigServer(session, queryManager.Object);
            RuntimeConfigProvider provider = AssertUnconfiguredHost(server.Server, session);

            bool initialized = await InitializeAsync(provider, versionTwo).WaitAsync(_timeout);

            Assert.IsTrue(initialized, "Startup's actual loaded-config handler must finish successfully.");
            Assert.IsTrue(session.IsReady, "No test calls AcceptConfiguration or MarkHostReady on this path.");
            RuntimeConfig config = provider.GetConfig();
            Assert.AreEqual(0, config.Entities.Count());
            Assert.AreEqual(0, config.Autoentities.Count());
            IMetadataProviderFactory factory = server.Services.GetRequiredService<IMetadataProviderFactory>();
            Assert.IsInstanceOfType(factory, typeof(MetadataProviderFactory));
            ISqlMetadataProvider metadata = factory.GetMetadataProvider(config.DefaultDataSourceName);
            Assert.IsInstanceOfType(metadata, typeof(MsSqlMetadataProvider));
            Assert.AreEqual(0, metadata.EntityToDatabaseObject.Count);
            Assert.AreEqual(0, metadata.SqlMetadataExceptions.Count);
            queryManager.Verify(manager => manager.GetQueryExecutor(DatabaseType.MSSQL), Times.Once);
            executor.VerifyNoOtherCalls();
            IOpenApiDocumentor documentor = server.Services.GetRequiredService<IOpenApiDocumentor>();
            Assert.IsTrue(documentor.TryGetDocument(out string? document), "Do not mask a swallowed OpenAPI initialization failure.");
            Assert.IsNotNull(document);
            using JsonDocument parsedDocument = JsonDocument.Parse(document);
            Assert.AreEqual(0, parsedDocument.RootElement.GetProperty("paths").EnumerateObject().Count());

            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED }, records.Select(record => record.Name).ToArray());
            EngineTelemetryEvent ready = records.Single(record => record.Name == READY);
            Assert.AreEqual(1L, ready.ConfigurationEpoch);
            Assert.AreEqual("late_configuration", ready.Properties["configuration_delivery"]);
            Assert.AreEqual("enabled", ready.Properties["runtime.rest.effective"]);
            Assert.AreEqual("disabled", ready.Properties["runtime.graphql.effective"]);
            Assert.AreEqual("0", ready.Properties["scale.entity_count"]);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task StartupAwaitsAndRejectsLateMetadataFailureWithoutReportingTerminalStartupFailure(bool versionTwo)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter);
            Mock<IQueryExecutor> executor = new(MockBehavior.Strict);
            Mock<IAbstractQueryManagerFactory> queryManager = CreateQueryManager(executor);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IMetadataProviderFactory> metadata = new(MockBehavior.Strict);
            metadata.Setup(factory => factory.InitializeAsync()).Returns(async () =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(_timeout);
                throw new InvalidOperationException(SENTINEL);
            });
            await using LateConfigServer server = CreateLateConfigServer(session, queryManager.Object, metadata.Object);
            RuntimeConfigProvider provider = AssertUnconfiguredHost(server.Server, session);

            Task<bool> initialization = InitializeAsync(provider, versionTwo);
            bool initialized;
            try
            {
                await entered.Task.WaitAsync(_timeout);
                Assert.IsFalse(initialization.IsCompleted);
                Assert.IsTrue(provider.TryGetLoadedConfig(out _), "Parsing happened before the metadata failure.");
                Assert.IsFalse(session.IsReady);
                AssertConfiguration(session, config: null, epoch: 0);
            }
            finally
            {
                release.TrySetResult();
                initialized = await initialization.WaitAsync(_timeout);
            }

            Assert.IsFalse(initialized);
            Assert.IsFalse(session.IsReady);
            Assert.IsTrue(session.IsEnabled, "Late-config failure must leave the telemetry session nonterminal.");
            AssertConfiguration(session, config: null, epoch: 0);
            Assert.IsFalse(server.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
            metadata.Verify(factory => factory.InitializeAsync(), Times.Once);
            metadata.VerifyNoOtherCalls();
            executor.VerifyNoOtherCalls();

            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, REJECTED, STOPPED }, records.Select(record => record.Name).ToArray());
            EngineTelemetryEvent rejected = records.Single(record => record.Name == REJECTED);
            Assert.AreEqual(0L, rejected.ConfigurationEpoch);
            Assert.AreEqual("configuration", rejected.Properties["failure_category"]);
            Assert.IsTrue(rejected.Properties.ContainsKey("failure_stage"));
            Assert.AreEqual("metadata", rejected.Properties["failure_stage"]);
            Assert.IsFalse(rejected.Properties.ContainsKey("dab_api_id"));
            Assert.IsFalse(rejected.Properties.ContainsKey("snapshot_schema"));
        }

        private static LateConfigServer CreateLateConfigServer(EngineTelemetrySession session,
            IAbstractQueryManagerFactory queryManager, IMetadataProviderFactory? failingMetadata = null)
        {
            // An explicitly missing, unique path avoids discovering an ambient developer config.
            // No config file or identity sidecar is written by this fixture.
            string missingConfigPath = Path.Combine(Path.GetTempPath(), "dab-telemetry-" + Guid.NewGuid().ToString("N") + ".json");
            IHost host = new HostBuilder()
                .UseEnvironment(Environments.Production)
                .ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConfigFileName"] = missingConfigPath,
                    ["CONNSTRING"] = CONNECTION_STRING
                }))
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHost(web => web
                    .UseSetting(WebHostDefaults.ApplicationKey, typeof(Startup).Assembly.GetName().Name)
                    .UseTestServer()
                    .UseStartup(context => new Startup(context.Configuration, NullLogger<Startup>.Instance) { ProductTelemetry = session })
                    .ConfigureTestServices(services =>
                    {
                        // Keep the generic host logger, but remove Startup's closed logger factories:
                        // those consult static customer sink settings left by unrelated tests.
                        foreach (ServiceDescriptor descriptor in services.Where(descriptor =>
                            descriptor.ServiceType.IsConstructedGenericType &&
                            descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ILogger<>)).ToArray())
                        {
                            services.Remove(descriptor);
                        }

                        services.Replace(ServiceDescriptor.Singleton(new DynamicLogLevelProvider()));
                        services.Replace(ServiceDescriptor.Singleton(queryManager));
                        if (failingMetadata is not null)
                        {
                            services.Replace(ServiceDescriptor.Singleton(failingMetadata));
                        }
                    }))
                .Build();

            try
            {
                host.Start();
                return new(host);
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        private static RuntimeConfigProvider AssertUnconfiguredHost(TestServer server, EngineTelemetrySession session)
        {
            Assert.AreSame(session, server.Services.GetRequiredService<EngineTelemetrySession>());
            Assert.IsTrue(server.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested);
            RuntimeConfigProvider provider = server.Services.GetRequiredService<RuntimeConfigProvider>();
            Assert.IsTrue(provider.IsLateConfigured, "Startup.Configure must install the late-config handler.");
            Assert.IsFalse(provider.TryGetLoadedConfig(out _));
            Assert.IsNotNull(server.Services.GetRequiredService<FileSystemRuntimeConfigLoader>().TelemetryReloadCompleted);
            Assert.IsTrue(session.IsEnabled);
            Assert.IsFalse(session.IsReady);
            AssertConfiguration(session, config: null, epoch: 0);
            return provider;
        }

        private static Mock<IAbstractQueryManagerFactory> CreateQueryManager(Mock<IQueryExecutor> executor)
        {
            Mock<IAbstractQueryManagerFactory> manager = new(MockBehavior.Strict);
            manager.Setup(factory => factory.GetQueryBuilder(DatabaseType.MSSQL)).Returns(new MsSqlQueryBuilder());
            manager.Setup(factory => factory.GetQueryExecutor(DatabaseType.MSSQL)).Returns(executor.Object);
            return manager;
        }

        private static Task<bool> InitializeAsync(RuntimeConfigProvider provider, bool versionTwo)
            => versionTwo
                ? provider.Initialize(CreateConfigJson(), schema: null, accessToken: null)
                : provider.Initialize(CreateConfigJson(), graphQLSchema: null, connectionString: CONNECTION_STRING,
                    accessToken: null, replacementSettings: null);

        private static EngineTelemetrySession CreateSession(CapturingExporter exporter)
            => EngineTelemetrySession.Create(
                exporterFactory: () => exporter,
                enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null,
                showNotice: () => { },
                resolveIdentity: _ => new(_apiId, "ephemeral"),
                startTimer: false);

        private static string CreateConfigJson(bool graphQl = false) => $$"""
            {
              "$schema": "{{RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK}}",
              "data-source": {
                "database-type": "mssql",
                "connection-string": "{{CONNECTION_STRING}}"
              },
              "runtime": {
                "host": { "mode": "development", "authentication": { "provider": "Unauthenticated" } },
                "rest": { "enabled": true, "path": "/api" },
                "graphql": { "enabled": {{(graphQl ? "true" : "false")}}, "path": "/graphql" },
                "mcp": { "enabled": false }
              },
              "entities": {}
            }
            """;

        private static void AssertConfiguration(EngineTelemetrySession session, RuntimeConfig? config, long epoch)
        {
            // Inspect the captured epoch without completing a synthetic served request.
            using EngineTelemetryRequestScope request = session.BeginRequest(
                EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            Assert.AreSame(config, request.Config);
            Assert.AreEqual(epoch, request.Configuration.Epoch);
        }

        private static async Task<EngineTelemetryEvent[]> DrainAsync(EngineTelemetrySession session, CapturingExporter exporter)
        {
            await session.StopAsync().WaitAsync(_timeout);
            await exporter.Disposed.Task.WaitAsync(_timeout);
            EngineTelemetryEvent[] records = exporter.Records.ToArray();
            Assert.IsFalse(records.Any(record => record.Properties.Any(property =>
                property.Key.Contains(SENTINEL, StringComparison.Ordinal) || property.Value.Contains(SENTINEL, StringComparison.Ordinal))));
            return records;
        }

        private sealed class LateConfigServer : IAsyncDisposable
        {
            public IHost Host { get; }
            public TestServer Server { get; }
            public IServiceProvider Services => Server.Services;

            public LateConfigServer(IHost host)
            {
                Host = host;
                Server = host.GetTestServer();
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    using CancellationTokenSource stopTimeout = new(_timeout);
                    await Host.StopAsync(stopTimeout.Token);
                }
                finally
                {
                    Host.Dispose();
                }
            }
        }

        private sealed class ReloadFixture : IDisposable
        {
            private readonly MockFileSystem _files = new();
            private readonly IDisposable _changeRegistration;
            public HotReloadEventHandler<HotReloadEventArgs> Handler { get; } = new();
            public CapturingExporter Exporter { get; } = new();
            public FileSystemRuntimeConfigLoader Loader { get; }
            public EngineTelemetrySession Session { get; }
            public RuntimeConfig InitialConfig { get; }
            public List<string> Trace { get; } = new();
            public List<(RuntimeConfig? Config, bool Accepted)> Completions { get; } = new();
            public List<(RuntimeConfig? Config, long Epoch)> SubscriberConfigurations { get; } = new();

            public ReloadFixture()
            {
                string path = _files.Path.Combine(_files.Directory.GetCurrentDirectory(), "engine-telemetry-reload.json");
                _files.AddFile(path, new MockFileData(CreateConfigJson()));
                // Suppress watcher construction only; Reload enters the unchanged watcher callback.
                Loader = new(_files, Handler, baseConfigFilePath: path, isCliLoader: true,
                    logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);
                Assert.IsTrue(Loader.TryLoadKnownConfig(out RuntimeConfig? initial));
                Assert.IsNotNull(initial);
                InitialConfig = initial;
                Session = CreateSession(Exporter);
                Loader.TelemetryCaptureEnabled = () => Session.IsEnabled;
                Session.AcceptConfiguration(initial);
                Session.MarkHostReady();
                Assert.IsTrue(Session.IsReady);
                _changeRegistration = ChangeToken.OnChange(Loader.GetChangeToken, () => ObserveSubscriber("change_token"));
                foreach (string eventName in _reloadEvents)
                {
                    Handler.Subscribe(eventName, (_, _) => ObserveSubscriber(eventName));
                }

                // Mirror Startup's forwarding delegate explicitly. This verifies the loader's
                // real notification boundary, not Startup.ConfigureServices' delegate wiring.
                // Capture observations for assertions outside Notify's exception-swallowing guard.
                Loader.TelemetryReloadCompleted = (config, accepted, stage) =>
                {
                    Completions.Add((config, accepted));
                    Trace.Add(accepted ? "accepted" : "rejected");
                    if (accepted && config is not null)
                    {
                        Session.AcceptConfiguration(config, "hot_reload", Loader.ConfigFilePath);
                    }
                    else
                    {
                        Session.ConfigurationChangeFailed(stage);
                    }
                };
            }

            public void Reload(string contents)
            {
                _files.File.WriteAllText(Loader.ConfigFilePath, contents);
                MethodInfo? callback = typeof(FileSystemRuntimeConfigLoader).GetMethod(
                    "OnNewFileContentsDetected", BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null, types: [typeof(object), typeof(EventArgs)], modifiers: null);
                Assert.IsNotNull(callback);
                callback.Invoke(Loader, [null, EventArgs.Empty]);
            }

            private void ObserveSubscriber(string eventName)
            {
                Trace.Add(eventName);
                using EngineTelemetryRequestScope request = Session.BeginRequest(
                    EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
                SubscriberConfigurations.Add((request.Config, request.Configuration.Epoch));
            }

            public void Dispose()
            {
                _changeRegistration.Dispose();
                Loader.Dispose();
                Session.Dispose();
            }
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            public ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();
            public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() => Disposed.TrySetResult();
        }
    }
}

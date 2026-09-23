// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Telemetry;
using Azure.DataApiBuilder.Service.Telemetry;
using Azure.DataApiBuilder.Service.Utilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IOPath = System.IO.Path;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Separate integration coverage using the real DAB startup, HTTP pipeline and SQL engine.
    /// Requires Windows and the current user's MSSQLLocalDB instance with database-creation
    /// permission. HTTP stays in TestServer; product telemetry stays in an in-memory exporter.
    /// This category intentionally does not join the database-free EngineTelemetry suite.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetryLocalDb")]
    [DoNotParallelize]
    public class EngineTelemetryLocalDbTests
    {
        private const string READY = "dab.engine.ready";
        private const string FIRST_SERVED = "dab.engine.first_request_served";
        private const string FIRST_SUCCESS = "dab.engine.first_successful_request";
        private const string SUMMARY = "dab.engine.usage_summary";
        private const string STOPPED = "dab.engine.stopped";
        private const string PRIVATE_ROLE = "LOCALDB_PRIVATE_ROLE_65cd20";
        private const string PRIVATE_VALUE = "LOCALDB_PRIVATE_VALUE_65cd20";
        private const string PRIVATE_FIELD = "LOCALDB_PRIVATE_MISSING_FIELD_65cd20";
        private const string PRIVATE_QUERY_NAME = "LOCALDB_PRIVATE_QUERY_65cd20";
        private const string DATA_QUERY = "query " + PRIVATE_QUERY_NAME + " { books { items { id title } } }";
        private const string FAILED_QUERY = "{ books { items { " + PRIVATE_FIELD + " } } }";
        private const string DISCOVERY_QUERY = "{ __schema { queryType { fields { name } } } }";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

        [DataTestMethod]
        [DataRow(false, false, "Books", false)]
        [DataRow(true, false, "Books", false)]
        [DataRow(false, true, "Books", false)]
        [DataRow(false, false, "swagger", false)]
        [DataRow(false, false, "swagger/books", false)]
        [DataRow(false, false, "mcp/books", false)]
        [DataRow(false, false, "Books", true)]
        public async Task RealHttpRequestsEmitMilestonesAndSqlUsageWithoutCustomerData(bool includeEmbeddingEndpoint, bool useAutoentities,
            string restEntityPath, bool includeHealthProbes)
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("EngineTelemetryLocalDb: platform_unavailable");
            }

            string databaseName = "dab_telemetry_" + Guid.NewGuid().ToString("N");
            string directory = IOPath.Combine(IOPath.GetTempPath(), databaseName);
            string configPath = IOPath.Combine(directory, "dab-config.json");
            string connectionString = CreateConnectionString(databaseName);
            bool databaseCreated = false;
            IHost? host = null;
            EngineTelemetrySession? session = null;
            using HostSettingsScope settings = new();

            try
            {
                await using (SqlConnection master = new(CreateConnectionString("master")))
                {
                    // Only failure to open the fixed local instance is an unmet prerequisite.
                    // DDL, DAB startup and assertion failures must fail rather than become skips.
                    try
                    {
                        await master.OpenAsync();
                    }
                    catch (SqlException)
                    {
                        Assert.Inconclusive("EngineTelemetryLocalDb: localdb_unavailable");
                    }
                    catch (PlatformNotSupportedException)
                    {
                        Assert.Inconclusive("EngineTelemetryLocalDb: localdb_unavailable");
                    }

                    using SqlCommand create = master.CreateCommand();
                    // The identifier is generated here, never supplied by configuration or the environment.
                    create.CommandText = $"CREATE DATABASE [{databaseName}];";
                    await create.ExecuteNonQueryAsync();
                    databaseCreated = true;
                }

                await using (SqlConnection database = new(connectionString))
                {
                    await database.OpenAsync();
                    using SqlCommand seed = database.CreateCommand();
                    seed.CommandText = """
                        CREATE TABLE dbo.TelemetryItems (id int NOT NULL PRIMARY KEY, title nvarchar(200) NOT NULL);
                        INSERT INTO dbo.TelemetryItems (id, title) VALUES (1, @title);
                        """;
                    seed.Parameters.Add("@title", SqlDbType.NVarChar, 200).Value = PRIVATE_VALUE;
                    await seed.ExecuteNonQueryAsync();
                }

                Directory.CreateDirectory(directory);
                JsonNode runtimeConfiguration = JsonNode.Parse(CreateConfig(connectionString))!;
                runtimeConfiguration["entities"]!["Books"]!["rest"]!["path"] = restEntityPath;
                if (restEntityPath == "mcp/books")
                {
                    runtimeConfiguration["runtime"]!["mcp"]!["path"] = "/api/mcp";
                }

                if (includeHealthProbes)
                {
                    runtimeConfiguration["runtime"]!["health"]!["enabled"] = true;
                    runtimeConfiguration["data-source"]!["health"] = new JsonObject { ["enabled"] = false };
                    runtimeConfiguration["entities"]!["Books"]!["health"] = new JsonObject { ["enabled"] = true, ["threshold-ms"] = 10000 };
                }

                if (useAutoentities)
                {
                    JsonNode template = runtimeConfiguration["entities"]!["Books"]!;
                    runtimeConfiguration["autoentities"] = new JsonObject
                    {
                        ["SyntheticAutoentities"] = new JsonObject
                        {
                            ["patterns"] = new JsonObject
                            {
                                ["include"] = new JsonArray("dbo.TelemetryItems"),
                                ["name"] = "Books"
                            },
                            ["template"] = new JsonObject
                            {
                                ["rest"] = template["rest"]!.DeepClone(),
                                ["graphql"] = template["graphql"]!.DeepClone()
                            },
                            ["permissions"] = template["permissions"]!.DeepClone()
                        }
                    };
                    runtimeConfiguration["entities"] = new JsonObject();
                }

                if (includeEmbeddingEndpoint)
                {
                    runtimeConfiguration["runtime"]!["embeddings"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["provider"] = "openai",
                        ["base-url"] = "https://synthetic.invalid/",
                        ["api-key"] = PRIVATE_VALUE,
                        ["endpoint"] = new JsonObject { ["enabled"] = true, ["path"] = "/embed" }
                    };
                }

                await File.WriteAllTextAsync(configPath, runtimeConfiguration.ToJsonString());
                CapturingExporter exporter = new();
                EmbeddingProviderHandler embeddingProvider = new();
                ResponseCompletionTracker completions = new();
                HealthProbeHandler healthProbes = new(completions);
                session = EngineTelemetrySession.Create(
                    exporterFactory: () => exporter,
                    enableSyntheticCollection: true,
                    configPath: configPath,
                    executionMode: "web",
                    readEnvironmentVariable: _ => null,
                    showNotice: () => { },
                    resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"),
                    startTimer: false);

                host = Program.CreateHostBuilder(
                    ["--ConfigFileName", configPath, "--no-https-redirect"],
                    runMcpStdio: false, mcpRole: null, productTelemetry: session)
                    .UseEnvironment("Development")
                    .ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            // Never inherit DAB_CONNSTRING pointing at another database/server.
                            ["CONNSTRING"] = connectionString
                        }))
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureWebHost(web => web
                        .UseSetting(WebHostDefaults.ApplicationKey, typeof(Program).Assembly.GetName().Name)
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddSingleton<IStartupFilter>(completions);
                            services.AddHttpClient(nameof(EmbeddingService))
                                .ConfigurePrimaryHttpMessageHandler(() => embeddingProvider);
                            if (includeHealthProbes)
                            {
                                services.AddHttpClient("ContextConfiguredHealthCheckClient")
                                    .ConfigurePrimaryHttpMessageHandler(() => healthProbes);
                            }
                        }))
                    .Build();

                using (CancellationTokenSource startupTimeout = new(TimeSpan.FromMinutes(2)))
                {
                    await host.StartAsync(startupTimeout.Token);
                }

                healthProbes.Server = host.GetTestServer();
                EngineTelemetryEvent ready = await exporter.WaitForAsync(READY);
                Assert.IsTrue(session.IsReady);
                Assert.AreEqual(1L, ready.ConfigurationEpoch);
                Assert.AreEqual("enabled", ready.Properties["runtime.rest.effective"]);
                Assert.AreEqual("enabled", ready.Properties["runtime.graphql.effective"]);
                Assert.AreEqual("enabled", ready.Properties["runtime.mcp.effective"]);
                Assert.AreEqual("mssql", ready.Properties["data_sources.types"]);
                RuntimeConfig servingConfig = host.Services.GetRequiredService<RuntimeConfigProvider>().GetConfig();
                Assert.AreEqual(1, servingConfig.Entities.Count());
                Assert.AreEqual("1", ready.Properties["scale.entity_count"],
                    "Readiness must snapshot the accepted post-metadata configuration, including generated entities.");
                Assert.AreEqual(useAutoentities ? "enabled" : "disabled", ready.Properties["integrations.autoentities.effective"]);

                using HttpClient client = host.GetTestClient();
                ServedResponse health = await completions.SendAsync(client, HttpMethod.Get, "/");
                Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
                using (JsonDocument body = JsonDocument.Parse(health.Body))
                {
                    Assert.AreEqual("Healthy", body.RootElement.GetProperty("status").GetString());
                }

                // The dedicated health client is forwarded into this same TestServer only in
                // the probe variant; the actual health controller/helper builds the requests.
                ServedResponse comprehensiveHealth = await completions.SendAsync(client, HttpMethod.Get, "/health");
                Assert.AreEqual(includeHealthProbes ? HttpStatusCode.OK : HttpStatusCode.NotFound, comprehensiveHealth.StatusCode);
                if (includeHealthProbes)
                {
                    Assert.AreEqual(3, healthProbes.Calls, "REST, GraphQL and MCP initialization health probes must execute through the real pipeline.");
                    using JsonDocument report = JsonDocument.Parse(comprehensiveHealth.Body);
                    Assert.AreEqual("Healthy", report.RootElement.GetProperty("status").GetString());
                    Assert.IsFalse(exporter.Records.Any(record => record.Name == FIRST_SERVED || record.Name == FIRST_SUCCESS),
                        "Health self-probes must not establish product usage milestones.");
                    client.DefaultRequestHeaders.Add(EngineTelemetryHealthProbe.HEADER_NAME, "synthetic-unrelated-marker");
                }

                ServedResponse openApi = await completions.SendAsync(client, HttpMethod.Get, "/api/openapi");
                Assert.AreEqual(HttpStatusCode.OK, openApi.StatusCode);
                using (JsonDocument body = JsonDocument.Parse(openApi.Body))
                {
                    Assert.IsTrue(body.RootElement.TryGetProperty("openapi", out _));
                    Assert.IsTrue(body.RootElement.GetProperty("paths").EnumerateObject().Any());
                }

                ServedResponse discovery = await completions.SendAsync(client, HttpMethod.Post, "/graphql", DISCOVERY_QUERY);
                Assert.AreEqual(HttpStatusCode.OK, discovery.StatusCode, discovery.Body);
                using (JsonDocument body = JsonDocument.Parse(discovery.Body))
                {
                    Assert.IsFalse(body.RootElement.TryGetProperty("errors", out _));
                    JsonElement fields = body.RootElement.GetProperty("data").GetProperty("__schema")
                        .GetProperty("queryType").GetProperty("fields");
                    Assert.IsTrue(fields.EnumerateArray().Any(field => field.GetProperty("name").GetString() == "books"),
                        "The real schema must expose the configured Books collection as books.");
                }

                // Start with a logical GraphQL failure. Legacy application/json negotiation
                // returns HTTP 200 for validation errors: HTTP success is not logical success.
                ServedResponse failure = await completions.SendAsync(client, HttpMethod.Post, "/graphql", FAILED_QUERY, PRIVATE_ROLE);
                Assert.AreEqual(HttpStatusCode.OK, failure.StatusCode);
                using (JsonDocument body = JsonDocument.Parse(failure.Body))
                {
                    JsonElement errors = body.RootElement.GetProperty("errors");
                    Assert.IsTrue(errors.EnumerateArray().Any(error => error.GetProperty("message").GetString()!
                        .Contains(PRIVATE_FIELD, StringComparison.Ordinal)), "The intended GraphQL validation error must have occurred.");
                }

                EngineTelemetryEvent firstServed = await exporter.WaitForAsync(FIRST_SERVED);
                Assert.AreEqual("graph_ql", firstServed.Properties["api"]);
                Assert.AreEqual("failure", firstServed.Properties["outcome"]);
                Assert.IsFalse(exporter.Records.Any(record => record.Name == FIRST_SUCCESS));

                ServedResponse rest = await completions.SendAsync(client, HttpMethod.Get, "/api/" + restEntityPath);
                AssertRow(rest, graphQL: false);
                EngineTelemetryEvent firstSuccess = await exporter.WaitForAsync(FIRST_SUCCESS);
                Assert.AreEqual("rest", firstSuccess.Properties["api"]);
                Assert.AreEqual("success", firstSuccess.Properties["outcome"]);

                ServedResponse graphQL = await completions.SendAsync(client, HttpMethod.Post, "/graphql", DATA_QUERY);
                AssertRow(graphQL, graphQL: true);

                if (includeEmbeddingEndpoint)
                {
                    // Use the actual Startup-mapped endpoint, controller and embedding service.
                    // Only the provider HTTP is fake; product scopes must come from the pipeline.
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        ServedResponse embedding = await completions.SendAsync(client, HttpMethod.Post, "/embed", text: PRIVATE_VALUE);
                        Assert.AreEqual(HttpStatusCode.OK, embedding.StatusCode, embedding.Body);
                        using JsonDocument body = JsonDocument.Parse(embedding.Body);
                        Assert.AreEqual(2, body.RootElement.GetProperty("embedding").GetArrayLength());
                    }

                    Assert.AreEqual(1, embeddingProvider.Calls, "The second endpoint call must use cached embeddings.");
                }

                // SendAsync waits for an outer OnCompleted callback after DAB's callbacks. Do
                // not stop collection merely because the client has received the response body.
                await session.StopAsync();
                await exporter.WaitForAsync(STOPPED);
                EngineTelemetryEvent[] records = exporter.Records.ToArray();
                AssertUsage(records, embeddingRequests: includeEmbeddingEndpoint ? 2 : 0);
                if (includeEmbeddingEndpoint)
                {
                    EngineTelemetryEvent[] cache = Summaries(records, "cache_lookup").ToArray();
                    Assert.IsTrue(Count(cache.Where(record => record.Properties["cache_layer"] == "level1" &&
                        record.Properties["cache_result"] == "miss"), "count") > 0, "The named embedding cache's real miss must be observed.");
                    Assert.IsTrue(Count(cache.Where(record => record.Properties["cache_layer"] == "level1" &&
                        record.Properties["cache_result"] == "hit"), "count") > 0, "The named embedding cache's real hit must be observed.");
                }

                AssertNoPrivateValues(records, databaseName, connectionString, configPath,
                    "TelemetryItems", "Books", PRIVATE_ROLE, PRIVATE_VALUE, PRIVATE_FIELD,
                    PRIVATE_QUERY_NAME, DATA_QUERY, FAILED_QUERY, DISCOVERY_QUERY, @"(localdb)\MSSQLLocalDB", session.HealthProbeToken!);
            }
            finally
            {
                bool hostDisposed = host is null;
                try
                {
                    if (session is not null)
                    {
                        await session.StopAsync();
                    }
                }
                finally
                {
                    try
                    {
                        try
                        {
                            if (host is not null)
                            {
                                using CancellationTokenSource stopTimeout = new(_timeout);
                                await host.StopAsync(stopTimeout.Token);
                            }
                        }
                        finally
                        {
                            host?.Dispose();
                            hostDisposed = true;
                        }
                    }
                    finally
                    {
                        session?.Dispose();
                        try
                        {
                            // Never force disconnects while the host may still be running.
                            if (databaseCreated && hostDisposed)
                            {
                                await DropDatabaseAsync(databaseName);
                            }
                        }
                        finally
                        {
                            if (hostDisposed && Directory.Exists(directory))
                            {
                                Directory.Delete(directory, recursive: true);
                            }
                        }
                    }
                }
            }
        }

        [TestMethod]
        public async Task RealMcpStdioRequestsEmitMilestonesAndSqlUsageWithoutCustomerData()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("EngineTelemetryLocalDb: platform_unavailable");
            }

            string databaseName = "dab_telemetry_" + Guid.NewGuid().ToString("N");
            string directory = IOPath.Combine(IOPath.GetTempPath(), databaseName);
            string configPath = IOPath.Combine(directory, "dab-config.json");
            string connectionString = CreateConnectionString(databaseName);
            bool databaseCreated = false;
            IHost? host = null;
            IHostApplicationLifetime? lifetime = null;
            Task<bool>? running = null;
            EngineTelemetrySession? session = null;
            using HostSettingsScope settings = new();
            using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(2));
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using McpStdoutWriter stdout = new(output);
            string protocol = string.Join(Environment.NewLine,
                Rpc(1, "initialize", new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "localdb-test", version = "1.0" } }),
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                Rpc(2, "ping"),
                Rpc(3, "tools/list"),
                Rpc(4, "tools/call", new { name = "describe_entities", arguments = new { entities = new[] { "Books" } } }),
                Rpc(5, "tools/call", new { name = "read_records", arguments = new { entity = "Books", first = 100 } }),
                Rpc(6, "tools/call", new { name = "read_records", arguments = new { entity = "Books", first = 100, select = PRIVATE_FIELD } }));
            using StringReader input = new(protocol);

            try
            {
                await using (SqlConnection master = new(CreateConnectionString("master")))
                {
                    // Only an unavailable local instance is a missing prerequisite, not a
                    // failed DDL statement, metadata initialization, protocol call or assertion.
                    try
                    {
                        await master.OpenAsync(deadline.Token);
                    }
                    catch (SqlException)
                    {
                        Assert.Inconclusive("EngineTelemetryLocalDb: localdb_unavailable");
                    }
                    catch (PlatformNotSupportedException)
                    {
                        Assert.Inconclusive("EngineTelemetryLocalDb: localdb_unavailable");
                    }

                    using SqlCommand create = master.CreateCommand();
                    create.CommandText = $"CREATE DATABASE [{databaseName}];";
                    await create.ExecuteNonQueryAsync(deadline.Token);
                    databaseCreated = true;
                }

                await using (SqlConnection database = new(connectionString))
                {
                    await database.OpenAsync(deadline.Token);
                    using SqlCommand seed = database.CreateCommand();
                    seed.CommandText = """
                        CREATE TABLE dbo.TelemetryItems (id int NOT NULL PRIMARY KEY, title nvarchar(200) NOT NULL);
                        INSERT INTO dbo.TelemetryItems (id, title) VALUES (1, @title);
                        """;
                    seed.Parameters.Add("@title", SqlDbType.NVarChar, 200).Value = PRIVATE_VALUE;
                    await seed.ExecuteNonQueryAsync(deadline.Token);
                }

                JsonNode config = JsonNode.Parse(CreateConfig(connectionString))!;
                config["runtime"]!["mcp"]!["dml-tools"] = new JsonObject
                {
                    ["describe-entities"] = true,
                    ["create-record"] = false,
                    ["read-records"] = true,
                    ["update-record"] = false,
                    ["delete-record"] = false,
                    ["execute-entity"] = false,
                    ["aggregate-records"] = false
                };
                config["entities"]!["Books"]!["mcp"] = true;
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(configPath, config.ToJsonString(), deadline.Token);

                CapturingExporter exporter = new();
                Guid apiId = Guid.NewGuid();
                int identityResolutions = 0;
                session = EngineTelemetrySession.Create(
                    exporterFactory: () => exporter,
                    enableSyntheticCollection: true,
                    configPath: configPath,
                    executionMode: "mcp_stdio",
                    readEnvironmentVariable: _ => null,
                    showNotice: () => { },
                    resolveIdentity: _ =>
                    {
                        Interlocked.Increment(ref identityResolutions);
                        return new(apiId, "ephemeral");
                    },
                    startTimer: false);

                host = Program.CreateHostBuilder(
                    ["--ConfigFileName", configPath, "--no-https-redirect"],
                    runMcpStdio: true, mcpRole: "anonymous", productTelemetry: session)
                    .UseEnvironment("Development")
                    .ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["CONNSTRING"] = connectionString }))
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureWebHost(web => web
                        .UseSetting(WebHostDefaults.ApplicationKey, typeof(Program).Assembly.GetName().Name)
                        .ConfigureTestServices(services =>
                        {
                            // Apply after real Startup.ConfigureServices; ordinary host service
                            // registration can otherwise be replaced by Startup's stdio server.
                            services.RemoveAll<McpStdoutWriter>();
                            services.AddSingleton(stdout);
                            services.RemoveAll<IMcpLogNotificationWriter>();
                            services.AddSingleton<IMcpLogNotificationWriter>(new McpLogNotificationWriter(stdout));
                            services.RemoveAll<IMcpStdioServer>();
                            services.AddSingleton<IMcpStdioServer>(provider => new McpStdioServer(
                                provider.GetRequiredService<McpToolRegistry>(), provider, input));
                        }))
                    .Build();

                Assert.AreSame(session, host.Services.GetRequiredService<EngineTelemetrySession>());
                Assert.AreSame(stdout, host.Services.GetServices<McpStdoutWriter>().Single(),
                    "The actual server must use the injected writer, never Console.OpenStandardOutput.");
                Assert.IsInstanceOfType(host.Services.GetRequiredService<IMcpStdioServer>(), typeof(McpStdioServer));
                RuntimeConfig loadedConfig = host.Services.GetRequiredService<RuntimeConfigProvider>().GetConfig();
                SqlConnectionStringBuilder actualConnection = new(loadedConfig.DataSource!.ConnectionString);
                Assert.AreEqual(@"(localdb)\MSSQLLocalDB", actualConnection.DataSource);
                Assert.AreEqual(databaseName, actualConnection.InitialCatalog,
                    "No ambient DAB_CONNSTRING may redirect this test to another database.");
                Assert.IsTrue(actualConnection.IntegratedSecurity);
                Assert.IsTrue(loadedConfig.McpDmlTools?.ReadRecords is true);
                McpToolRegistry registry = host.Services.GetRequiredService<McpToolRegistry>();
                ReadRecordsTool readTool = host.Services.GetServices<IMcpTool>().OfType<ReadRecordsTool>().Single();
                Assert.IsFalse(registry.TryGetTool("read_records", out _),
                    "The real stdio helper, not the test or a started hosted service, must initialize the registry.");
                lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                CancellationToken applicationStarted = lifetime.ApplicationStarted;
                Assert.IsFalse(applicationStarted.IsCancellationRequested);
                Assert.IsTrue(session.IsEnabled);
                Assert.IsFalse(session.IsReady);

                // Build only: no Start/Run and no HTTP listener. The real helper initializes
                // metadata, registers the actual tools, marks ready, serves until input EOF,
                // then flushes telemetry and disposes the host. Do not synthesize observations.
                IHost stdioHost = host;
                using (CancellationTokenRegistration stopOnTimeout = deadline.Token.Register(lifetime.StopApplication))
                {
                    running = Task.Run(() => McpStdioHelper.RunMcpStdioHost(stdioHost));
                    Assert.IsTrue(await running.WaitAsync(deadline.Token), "The real MCP stdio host must complete successfully.");
                }

                Assert.IsFalse(applicationStarted.IsCancellationRequested, "MCP stdio must not start the web host.");
                Assert.IsTrue(registry.TryGetTool("read_records", out IMcpTool? registered));
                Assert.AreSame(readTool, registered, "ReadRecordsTool must come from Startup's automatic tool registration.");

                JsonElement[] responses = output.ToString()
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line =>
                    {
                        using JsonDocument response = JsonDocument.Parse(line);
                        return response.RootElement.Clone();
                    }).ToArray();
                CollectionAssert.AreEqual(Enumerable.Range(1, 6).ToArray(),
                    responses.Select(response => response.GetProperty("id").GetInt32()).ToArray());
                Assert.IsTrue(responses.All(response => response.GetProperty("jsonrpc").GetString() == "2.0" &&
                    !response.TryGetProperty("error", out _)), "Every request must reach its intended protocol or tool handler.");
                Assert.AreEqual("2025-03-26", responses[0].GetProperty("result").GetProperty("protocolVersion").GetString());
                Assert.IsTrue(responses[1].GetProperty("result").GetProperty("ok").GetBoolean());
                JsonElement tools = responses[2].GetProperty("result").GetProperty("tools");
                CollectionAssert.AreEquivalent(new[] { "describe_entities", "read_records" },
                    tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray());

                JsonElement discovery = ToolPayload(responses[3], isError: false);
                Assert.AreEqual("success", discovery.GetProperty("status").GetString());
                Assert.AreEqual(1, discovery.GetProperty("entities").GetArrayLength());
                Assert.AreEqual("Book", discovery.GetProperty("entities")[0].GetProperty("name").GetString(),
                    "MCP discovery exposes the configured singular GraphQL type name.");
                JsonElement read = ToolPayload(responses[4], isError: false);
                Assert.AreEqual("success", read.GetProperty("status").GetString());
                Assert.AreEqual("Books", read.GetProperty("entity").GetString());
                JsonElement rows = read.GetProperty("result").GetProperty("value");
                Assert.AreEqual(1, rows.GetArrayLength());
                Assert.AreEqual(1, rows[0].GetProperty("id").GetInt32());
                Assert.AreEqual(PRIVATE_VALUE, rows[0].GetProperty("title").GetString(),
                    "The real stdio tool must return the row from this test's unique LocalDB database.");
                JsonElement failure = ToolPayload(responses[5], isError: true);
                Assert.AreEqual("error", failure.GetProperty("status").GetString());
                Assert.AreEqual("read_records", failure.GetProperty("toolName").GetString());
                Assert.AreEqual("BadRequest", failure.GetProperty("error").GetProperty("type").GetString());
                Assert.AreEqual("Invalid field to be returned requested: " + PRIVATE_FIELD,
                    failure.GetProperty("error").GetProperty("message").GetString());

                // RunMcpStdioHost has awaited StopAsync and drained the in-memory exporter.
                EngineTelemetryEvent[] records = exporter.Records.ToArray();
                EngineTelemetryEvent started = OnlyEvent(records, "dab.engine.process_started");
                EngineTelemetryEvent ready = OnlyEvent(records, READY);
                EngineTelemetryEvent served = OnlyEvent(records, FIRST_SERVED);
                EngineTelemetryEvent success = OnlyEvent(records, FIRST_SUCCESS);
                EngineTelemetryEvent stopped = OnlyEvent(records, STOPPED);
                Assert.IsTrue(started.Sequence < ready.Sequence && ready.Sequence < served.Sequence &&
                    served.Sequence < success.Sequence && success.Sequence < stopped.Sequence);
                Assert.AreEqual(0L, started.ConfigurationEpoch);
                Assert.IsTrue(records.Where(record => record != started).All(record => record.ConfigurationEpoch == 1));
                Assert.AreEqual("enabled", ready.Properties["runtime.mcp.effective"]);
                Assert.AreEqual("mssql", ready.Properties["data_sources.types"]);
                Assert.AreEqual("startup", ready.Properties["configuration_delivery"]);
                Assert.AreEqual("graceful_shutdown", stopped.Properties["reason"]);
                Assert.IsTrue(new[] { served, success }.All(record => record.Properties["api"] == "mcp" &&
                    record.Properties["transport"] == "stdio" && record.Properties["outcome"] == "success"));
                Assert.IsTrue(records.All(record => record.IsSynthetic && record.SessionId == started.SessionId &&
                    record.Properties["execution_mode"] == "mcp_stdio" && record.Properties["sender_dropped_events"] == "0"));
                Assert.AreEqual(records.Length, records.Select(record => record.EventId).Distinct().Count());
                Assert.AreEqual(1, identityResolutions);
                Assert.IsTrue(records.Where(record => record != started).All(record =>
                    record.Properties["dab_api_id"] == apiId.ToString("D") && record.Properties["dab_api_id_stability"] == "ephemeral"));
                Assert.IsFalse(records.Any(record => record.Name == "dab.engine.startup_failed"));
                Assert.IsFalse(Summaries(records, "collection_loss").Any());

                EngineTelemetryEvent[] requests = Summaries(records, "request").ToArray();
                AssertOutcomes(requests, count: 2, successes: 1, failures: 1);
                Assert.AreEqual(2L, Count(requests, "timed_count"));
                Assert.IsTrue(requests.All(record => record.Properties["api"] == "mcp" &&
                    record.Properties["transport"] == "stdio" && record.Properties["role_class"] == "anonymous"),
                    "Initialize, ping, tools/list and describe_entities must not count as data requests.");
                Assert.IsFalse(Summaries(records, "http_outcome").Any(),
                    "The stdio authorization HttpContext is not an HTTP response.");
                EngineTelemetryEvent[] operations = Summaries(records, "operation").ToArray();
                AssertOutcomes(operations, count: 2, successes: 1, failures: 1);
                Assert.IsTrue(operations.All(record => record.Properties["api"] == "mcp" &&
                    record.Properties["operation"] == "read" && record.Properties["provider"] == "ms_sql" &&
                    record.Properties["object_type"] == "table"), "Nested SQL execution must not double-count the tool operation.");
                EngineTelemetryEvent[] attempts = Summaries(records, "database_attempt").ToArray();
                Assert.IsTrue(Count(attempts, "count") > 0 && Count(attempts, "success") > 0,
                    "The uncached read_records call must reach the real SQL command executor.");
                Assert.IsTrue(attempts.All(record => record.Properties["provider"] == "ms_sql"));
                AssertNoPrivateValues(records, databaseName, connectionString, configPath, "TelemetryItems", "Books",
                    PRIVATE_ROLE, PRIVATE_VALUE, PRIVATE_FIELD, protocol, @"(localdb)\MSSQLLocalDB");
            }
            finally
            {
                bool hostDisposed = host is null;
                try
                {
                    if (running is not null && !running.IsCompleted)
                    {
                        lifetime?.StopApplication();
                        try
                        {
                            await running.WaitAsync(_timeout);
                        }
                        catch (OperationCanceledException) when (running.IsCompleted)
                        {
                            // Deadline cancellation is already reported by the test's wait.
                        }
                    }
                }
                finally
                {
                    try
                    {
                        // The helper normally owns disposal. Also cover failures before it
                        // runs, but never dispose services or drop a DB under an active helper.
                        if (running is null || running.IsCompleted)
                        {
                            host?.Dispose();
                            hostDisposed = true;
                        }
                    }
                    finally
                    {
                        if (hostDisposed)
                        {
                            session?.Dispose();
                            try
                            {
                                if (databaseCreated)
                                {
                                    await DropDatabaseAsync(databaseName).WaitAsync(_timeout);
                                }
                            }
                            finally
                            {
                                if (Directory.Exists(directory))
                                {
                                    Directory.Delete(directory, recursive: true);
                                }
                            }
                        }
                    }
                }
            }

            static string Rpc(int id, string method, object? parameters = null)
                => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });

            static JsonElement ToolPayload(JsonElement response, bool isError)
            {
                JsonElement result = response.GetProperty("result");
                Assert.AreEqual(isError, result.TryGetProperty("isError", out JsonElement flag) && flag.GetBoolean());
                JsonElement content = result.GetProperty("content");
                Assert.AreEqual(1, content.GetArrayLength());
                Assert.AreEqual("text", content[0].GetProperty("type").GetString());
                using JsonDocument payload = JsonDocument.Parse(content[0].GetProperty("text").GetString()!);
                return payload.RootElement.Clone();
            }
        }

        private static string CreateConnectionString(string databaseName) => new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = true,
            Pooling = false,
            ConnectTimeout = 30,
            ConnectRetryCount = 0
        }.ConnectionString;

        private static string CreateConfig(string connectionString) => $$"""
            {
              "data-source": {
                "database-type": "mssql",
                "connection-string": {{JsonSerializer.Serialize(connectionString)}}
              },
              "runtime": {
                "rest": { "enabled": true, "path": "/api" },
                "graphql": { "enabled": true, "path": "/graphql", "allow-introspection": true },
                "mcp": { "enabled": true, "path": "/mcp" },
                "host": {
                  "mode": "development",
                  "authentication": { "provider": "Simulator" },
                  "cors": { "origins": [], "allow-credentials": false }
                },
                "cache": { "enabled": false },
                "health": { "enabled": false },
                "telemetry": {
                  "application-insights": { "enabled": false },
                  "open-telemetry": { "enabled": false },
                  "azure-log-analytics": { "enabled": false },
                  "file": { "enabled": false }
                }
              },
              "entities": {
                "Books": {
                  "source": { "object": "dbo.TelemetryItems", "type": "table" },
                  "rest": { "enabled": true },
                  "graphql": { "enabled": true, "type": { "singular": "Book", "plural": "Books" } },
                  "permissions": [
                    { "role": "anonymous", "actions": [ "read" ] },
                    { "role": "{{PRIVATE_ROLE}}", "actions": [ "read" ] }
                  ]
                }
              }
            }
            """;

        private static async Task DropDatabaseAsync(string databaseName)
        {
            const string PREFIX = "dab_telemetry_";
            if (!databaseName.StartsWith(PREFIX, StringComparison.Ordinal) ||
                !Guid.TryParseExact(databaseName[PREFIX.Length..], "N", out _))
            {
                throw new InvalidOperationException("Refusing to clean up a database not owned by this test.");
            }

            await using SqlConnection master = new(CreateConnectionString("master"));
            await master.OpenAsync();
            using SqlCommand drop = master.CreateCommand();
            drop.CommandText = $"""
                IF DB_ID(@databaseName) IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END;
                """;
            drop.Parameters.Add("@databaseName", SqlDbType.NVarChar, 128).Value = databaseName;
            await drop.ExecuteNonQueryAsync();
        }

        private static void AssertRow(ServedResponse response, bool graphQL)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(response.Body);
            Assert.IsFalse(body.RootElement.TryGetProperty("errors", out _));
            JsonElement rows = graphQL
                ? body.RootElement.GetProperty("data").GetProperty("books").GetProperty("items")
                : body.RootElement.GetProperty("value");
            Assert.AreEqual(1, rows.GetArrayLength());
            Assert.AreEqual(1, rows[0].GetProperty("id").GetInt32());
            Assert.AreEqual(PRIVATE_VALUE, rows[0].GetProperty("title").GetString(),
                "The actual API must return the row from this test's unique database.");
        }

        private static void AssertUsage(EngineTelemetryEvent[] records, int embeddingRequests = 0)
        {
            EngineTelemetryEvent started = OnlyEvent(records, "dab.engine.process_started");
            EngineTelemetryEvent ready = OnlyEvent(records, READY);
            EngineTelemetryEvent served = OnlyEvent(records, FIRST_SERVED);
            EngineTelemetryEvent success = OnlyEvent(records, FIRST_SUCCESS);
            EngineTelemetryEvent stopped = OnlyEvent(records, STOPPED);
            Assert.IsTrue(started.Sequence < ready.Sequence && ready.Sequence < served.Sequence &&
                served.Sequence < success.Sequence && success.Sequence < stopped.Sequence);
            Assert.AreEqual("http", served.Properties["transport"]);
            Assert.AreEqual("http", success.Properties["transport"]);
            Assert.IsTrue(records.All(record => record.IsSynthetic && record.SessionId == started.SessionId &&
                record.Properties["sender_dropped_events"] == "0"));
            Assert.AreEqual(records.Length, records.Select(record => record.EventId).Distinct().Count());

            EngineTelemetryEvent[] summaries = records.Where(record => record.Name == SUMMARY).ToArray();
            Assert.IsTrue(summaries.All(record => record.ConfigurationEpoch == 1));
            Assert.IsFalse(summaries.Any(record => record.Properties["family"] == "collection_loss"));
            EngineTelemetryEvent[] requests = Summaries(records, "request").ToArray();
            Assert.AreEqual(3L + embeddingRequests, Count(requests, "count"), "Health, OpenAPI and introspection must not add data requests.");
            Assert.IsTrue(requests.All(record => record.Properties["transport"] == "http"));
            AssertOutcomes(requests.Where(record => record.Properties["api"] == "rest" &&
                record.Properties["role_class"] == "anonymous"), count: 1 + embeddingRequests, successes: 1 + embeddingRequests, failures: 0);
            AssertOutcomes(requests.Where(record => record.Properties["api"] == "graph_ql" &&
                record.Properties["role_class"] == "anonymous"), count: 1, successes: 1, failures: 0);
            AssertOutcomes(requests.Where(record => record.Properties["api"] == "graph_ql" &&
                record.Properties["role_class"] == "custom"), count: 1, successes: 0, failures: 1);

            EngineTelemetryEvent[] http = Summaries(records, "http_outcome").ToArray();
            Assert.AreEqual(3L + embeddingRequests, Count(http, "count"));
            Assert.IsTrue(http.All(record => record.Properties["http_status_class"] == "success"),
                "The GraphQL failure is logical, despite its completed 2xx HTTP response.");
            foreach (string api in new[] { "rest", "graph_ql" })
            {
                EngineTelemetryEvent[] operations = Summaries(records, "operation")
                    .Where(record => record.Properties["api"] == api).ToArray();
                Assert.IsTrue(Count(operations, "success") > 0, "Both real API paths must execute a logical SQL operation.");
                Assert.IsTrue(operations.All(record => record.Properties["operation"] == "read" &&
                    record.Properties["provider"] == "ms_sql" && record.Properties["object_type"] == "table"));
            }

            EngineTelemetryEvent[] attempts = Summaries(records, "database_attempt").ToArray();
            Assert.IsTrue(Count(attempts, "count") >= 2 && Count(attempts, "success") >= 2,
                "Uncached REST and GraphQL reads must reach the real SQL command executor.");
            Assert.IsTrue(attempts.All(record => record.Properties["provider"] == "ms_sql"));
            AssertOutcomes(Summaries(records, "embedding"), count: embeddingRequests, successes: embeddingRequests, failures: 0);
        }

        private static EngineTelemetryEvent OnlyEvent(EngineTelemetryEvent[] records, string name)
        {
            EngineTelemetryEvent[] found = records.Where(record => record.Name == name).ToArray();
            Assert.AreEqual(1, found.Length, $"Expected exactly one {name} event.");
            return found[0];
        }

        private static IEnumerable<EngineTelemetryEvent> Summaries(EngineTelemetryEvent[] records, string family)
            => records.Where(record => record.Name == SUMMARY && record.Properties["family"] == family);

        private static long Count(IEnumerable<EngineTelemetryEvent> records, string field)
            => records.Sum(record => long.Parse(record.Properties[field], CultureInfo.InvariantCulture));

        private static void AssertOutcomes(IEnumerable<EngineTelemetryEvent> records, long count, long successes, long failures)
        {
            EngineTelemetryEvent[] series = records.ToArray();
            Assert.AreEqual(count, Count(series, "count"));
            Assert.AreEqual(successes, Count(series, "success"));
            Assert.AreEqual(failures, Count(series, "failure"));
            Assert.AreEqual(0L, Count(series, "unknown") + Count(series, "partial_failure") + Count(series, "canceled"));
        }

        private static void AssertNoPrivateValues(EngineTelemetryEvent[] records, params string[] privateValues)
        {
            // Inspect unescaped keys and values as well as names; JSON escaping must not hide a leak.
            string content = string.Join("\n", records.SelectMany(record =>
                record.Properties.Keys.Concat(record.Properties.Values).Prepend(record.Name)));
            foreach (string value in privateValues)
            {
                Assert.IsFalse(content.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "Product telemetry leaked a synthetic customer value.");
            }
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            private readonly ConcurrentDictionary<string, TaskCompletionSource<EngineTelemetryEvent>> _observed = new();
            public ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                Signal(record.Name).TrySetResult(record);
                return ValueTask.FromResult(true);
            }

            public Task<EngineTelemetryEvent> WaitForAsync(string name) => Signal(name).Task.WaitAsync(_timeout);
            public void Dispose() { }

            private TaskCompletionSource<EngineTelemetryEvent> Signal(string name)
                => _observed.GetOrAdd(name, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        private sealed record ServedResponse(HttpStatusCode StatusCode, string Body);

        private sealed class EmbeddingProviderHandler : HttpMessageHandler
        {
            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[{"index":0,"embedding":[0.25,0.75]}]}""", Encoding.UTF8, "application/json")
                });
            }
        }

        private sealed class HealthProbeHandler(ResponseCompletionTracker completions) : HttpMessageHandler
        {
            public TestServer? Server { get; set; }
            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return completions.ForwardAsync(Server ?? throw new InvalidOperationException("Synthetic host is not ready."),
                    request, cancellationToken);
            }
        }

        /// <summary>
        /// Adds observation only, never calls telemetry collection APIs. OnCompleted callbacks
        /// run LIFO: registering outside Startup means this signal follows DAB's completion hooks.
        /// </summary>
        private sealed class ResponseCompletionTracker : IStartupFilter
        {
            private const string HEADER = "X-Engine-Telemetry-Test-Id";
            private readonly ConcurrentDictionary<string, TaskCompletionSource> _pending = new();

            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use(async (HttpContext context, RequestDelegate nextRequest) =>
                {
                    if (_pending.TryGetValue(context.Request.Headers[HEADER].ToString(), out TaskCompletionSource? completed))
                    {
                        context.Response.OnCompleted(() =>
                        {
                            completed.TrySetResult();
                            return Task.CompletedTask;
                        });
                    }

                    await nextRequest(context);
                });
                next(app);
            };

            public async Task<ServedResponse> SendAsync(HttpClient client, HttpMethod method, string path,
                string? query = null, string role = "anonymous", string? text = null)
            {
                string id = Guid.NewGuid().ToString("N");
                TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.IsTrue(_pending.TryAdd(id, completed));
                try
                {
                    using HttpRequestMessage request = new(method, path);
                    request.Headers.Add(HEADER, id);
                    // Simulator otherwise selects authenticated; explicitly exercise the anonymous permission.
                    request.Headers.Add("X-MS-API-ROLE", role);
                    request.Headers.Accept.ParseAdd("application/json");
                    if (text is not null)
                    {
                        request.Content = new StringContent(text, Encoding.UTF8, "text/plain");
                    }
                    else if (query is not null)
                    {
                        request.Content = JsonContent.Create(new { query });
                    }

                    using CancellationTokenSource timeout = new(_timeout);
                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
                    string body = await response.Content.ReadAsStringAsync(timeout.Token);
                    await completed.Task.WaitAsync(timeout.Token);
                    return new(response.StatusCode, body);
                }
                finally
                {
                    _pending.TryRemove(id, out _);
                }
            }

            internal async Task<HttpResponseMessage> ForwardAsync(TestServer server, HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string id = Guid.NewGuid().ToString("N");
                TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.IsTrue(_pending.TryAdd(id, completed));
                try
                {
                    using HttpClient client = server.CreateClient();
                    using HttpRequestMessage forwarded = new(request.Method, request.RequestUri!.PathAndQuery) { Content = request.Content };
                    foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                    {
                        forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    forwarded.Headers.Add(HEADER, id);
                    HttpResponseMessage response = await client.SendAsync(forwarded, cancellationToken);
                    await completed.Task.WaitAsync(cancellationToken);
                    return response;
                }
                finally
                {
                    _pending.TryRemove(id, out _);
                }
            }
        }

        /// <summary>
        /// Startup's logger factory reads these static settings before loading the new config.
        /// Isolate earlier tests' sinks and log-level state, and restore everything on every exit.
        /// </summary>
        private sealed class HostSettingsScope : IDisposable
        {
            private readonly ApplicationInsightsOptions _appInsights = Startup.AppInsightsOptions;
            private readonly OpenTelemetryOptions _openTelemetry = Startup.OpenTelemetryOptions;
            private readonly AzureLogAnalyticsOptions _logAnalytics = Startup.AzureLogAnalyticsOptions;
            private readonly FileSinkOptions _file = Startup.FileSinkOptions;
            private readonly DynamicLogLevelProvider _logLevelProvider = Program.LogLevelProvider;
            private readonly LogLevel _minimumLogLevel = Startup.MinimumLogLevel;
            private readonly bool _cliOverriding = Startup.IsCliOverriding;
            private readonly bool _httpsRedirectionDisabled = Program.IsHttpsRedirectionDisabled;

            public HostSettingsScope()
            {
                Startup.AppInsightsOptions = new();
                Startup.OpenTelemetryOptions = new();
                Startup.AzureLogAnalyticsOptions = new();
                Startup.FileSinkOptions = new();
                Program.LogLevelProvider = new();
                Program.LogLevelProvider.SetInitialLogLevel(LogLevel.None, isCliOverriding: true);
            }

            public void Dispose()
            {
                Startup.AppInsightsOptions = _appInsights;
                Startup.OpenTelemetryOptions = _openTelemetry;
                Startup.AzureLogAnalyticsOptions = _logAnalytics;
                Startup.FileSinkOptions = _file;
                Program.LogLevelProvider = _logLevelProvider;
                Startup.MinimumLogLevel = _minimumLogLevel;
                Startup.IsCliOverriding = _cliOverriding;
                // No production setter is exposed for this process-wide command-line setting.
                typeof(Program).GetProperty(nameof(Program.IsHttpsRedirectionDisabled))!.SetValue(null, _httpsRedirectionDisabled);
            }
        }
    }
}

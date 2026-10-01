// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IOPath = System.IO.Path;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Explicit live-cloud smoke test. Runs the built standalone engine, not an injected
    /// collector, against a newly created LocalDB fixture. Normal test runs skip this class.
    /// This test proves serving and graceful exit; its receipt must be reconciled with the
    /// selected Application Insights resource AND its linked workspace to prove delivery.
    /// No cloud resource, connection string or management credential is embedded here.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetryCloud")]
    [DoNotParallelize]
    public class EngineTelemetryCloudTests
    {
        private const string ENABLE_VARIABLE = "DAB_TELEMETRY_CLOUD_TEST";
        private const string ENGINE_VARIABLE = "DAB_TELEMETRY_CLOUD_TEST_ENGINE";
        private const string PRIVATE_VALUE = "CLOUD_SMOKE_PRIVATE_VALUE_a483b2";
        private const string PRIVATE_FIELD = "CLOUD_SMOKE_PRIVATE_FIELD_a483b2";
        private const string DATABASE_PREFIX = "dab_cloud_smoke_";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public async Task StandaloneStdioServesSyntheticSqlTrafficWithRealExporter()
        {
            if (Environment.GetEnvironmentVariable(ENABLE_VARIABLE) != "1")
            {
                Assert.Inconclusive("Live Azure telemetry is disabled. This test requires explicit DAB_TELEMETRY_CLOUD_TEST=1.");
            }

            Assert.IsTrue(OperatingSystem.IsWindows(), "The live smoke test requires Windows LocalDB.");
            Assert.IsFalse(ProductTelemetryPolicy.IsOptedOut(), "The global product opt-out must not be overridden by this test.");
            string? destination = Environment.GetEnvironmentVariable(EngineTelemetryHosting.CONNECTION_STRING_VARIABLE);
            Assert.IsTrue(ApplicationInsightsTelemetryDestination.TryParse(destination, out _),
                "Provide the approved dedicated test destination in the product connection-string environment variable.");
            string? engine = Environment.GetEnvironmentVariable(ENGINE_VARIABLE);
            Assert.IsTrue(engine is not null && IOPath.IsPathFullyQualified(engine) && File.Exists(engine) &&
                IOPath.GetFileName(engine) == "Azure.DataApiBuilder.Service.dll", "Specify the exact built standalone engine path.");

            // Pin the executed product artifact in the receipt, without reporting its local path.
            string engineSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(engine!)));
            string fixtureId = Guid.NewGuid().ToString("N");
            string databaseName = DATABASE_PREFIX + fixtureId;
            string directory = IOPath.Combine(IOPath.GetTempPath(), databaseName);
            string configPath = IOPath.Combine(directory, "dab-config.json");
            string connectionString = CreateConnectionString(databaseName);
            bool databaseCreated = false;
            bool processStarted = false;
            using Process process = new();
            using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
            try
            {
                await ExecuteSqlAsync("master", $"CREATE DATABASE [{databaseName}];", deadline.Token);
                databaseCreated = true;
                await ExecuteSqlAsync(databaseName, """
                    CREATE TABLE dbo.CloudSmokeItems (id int NOT NULL PRIMARY KEY, title nvarchar(100) NOT NULL);
                    INSERT dbo.CloudSmokeItems (id, title) VALUES (1, N'CLOUD_SMOKE_PRIVATE_VALUE_a483b2');
                    """, deadline.Token);
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(configPath, CreateConfiguration(), deadline.Token);

                ProcessStartInfo startInfo = new("dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = directory
                };
                foreach (string argument in new[] { engine!, "--ConfigFileName", configPath, "--mcp-stdio", "--no-https-redirect", "--log-level", "none" })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                // A caller's ambient database override must never select a different database.
                // Customer diagnostics are disabled in the fixture; product credentials stay in
                // this child's environment, not command arguments, config files or test output.
                foreach (string variable in startInfo.Environment.Keys.Where(name =>
                    name.StartsWith("DAB_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("APPLICATIONINSIGHTS_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase) ||
                    name is "ASPNETCORE_URLS" or "ASPNETCORE_ENVIRONMENT" or "DOTNET_ENVIRONMENT").ToArray())
                {
                    startInfo.Environment.Remove(variable);
                }

                startInfo.Environment[EngineTelemetryHosting.TEST_MODE_VARIABLE] = "1";
                startInfo.Environment[EngineTelemetryHosting.CONNECTION_STRING_VARIABLE] = destination;
                startInfo.Environment[EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE] = "true";
                startInfo.Environment[EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE] = "true";
                startInfo.Environment["DAB_CLOUD_SMOKE_DB"] = connectionString;
                process.StartInfo = startInfo;
                DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
                Assert.IsTrue(process.Start(), "The standalone engine could not start.");
                processStarted = true;
                Task<string> stderr = process.StandardError.ReadToEndAsync();

                JsonElement initialized = await RequestAsync(process, 1, "initialize", new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { },
                    clientInfo = new { name = "synthetic-telemetry-smoke", version = "1.0" }
                }, deadline.Token);
                Assert.AreEqual("2025-03-26", initialized.GetProperty("protocolVersion").GetString());
                await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
                JsonElement ping = await RequestAsync(process, 2, "ping", new { }, deadline.Token);
                Assert.IsTrue(ping.GetProperty("ok").GetBoolean());
                JsonElement tools = await RequestAsync(process, 3, "tools/list", new { }, deadline.Token);
                CollectionAssert.AreEquivalent(new[] { "describe_entities", "read_records" },
                    tools.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray());
                JsonElement discovery = ToolPayload(await RequestAsync(process, 4, "tools/call", new
                {
                    name = "describe_entities",
                    arguments = new { entities = new[] { "SmokeItems" } }
                }, deadline.Token), isError: false);
                Assert.AreEqual(1, discovery.GetProperty("entities").GetArrayLength());

                JsonElement read = ToolPayload(await RequestAsync(process, 5, "tools/call", new
                {
                    name = "read_records",
                    arguments = new { entity = "SmokeItems", first = 10 }
                }, deadline.Token), isError: false);
                JsonElement rows = read.GetProperty("result").GetProperty("value");
                Assert.AreEqual(1, rows.GetArrayLength());
                Assert.AreEqual(1, rows[0].GetProperty("id").GetInt32());
                Assert.AreEqual(PRIVATE_VALUE, rows[0].GetProperty("title").GetString(), "The tool must return the actual seeded SQL row.");

                JsonElement empty = ToolPayload(await RequestAsync(process, 6, "tools/call", new
                {
                    name = "read_records",
                    arguments = new { entity = "SmokeItems", first = 10, filter = "id eq 999" }
                }, deadline.Token), isError: false);
                Assert.AreEqual(0, empty.GetProperty("result").GetProperty("value").GetArrayLength(), "An empty SQL result is still a successful request.");

                JsonElement rejected = ToolPayload(await RequestAsync(process, 7, "tools/call", new
                {
                    name = "read_records",
                    arguments = new { entity = "SmokeItems", select = PRIVATE_FIELD }
                }, deadline.Token), isError: true);
                Assert.AreEqual("BadRequest", rejected.GetProperty("error").GetProperty("type").GetString());
                Assert.AreEqual("Invalid field to be returned requested: " + PRIVATE_FIELD,
                    rejected.GetProperty("error").GetProperty("message").GetString());

                // EOF is the real stdio host's graceful shutdown, including its bounded flush.
                process.StandardInput.Close();
                await process.WaitForExitAsync(deadline.Token).WaitAsync(_timeout, deadline.Token);
                Assert.AreEqual(0, process.ExitCode, "The real engine must shut down cleanly.");
                Assert.IsNull(await process.StandardOutput.ReadLineAsync(deadline.Token), "Only protocol responses may reach stdout.");
                Assert.IsTrue((await stderr).Contains("DAB synthetic product telemetry is enabled", StringComparison.Ordinal),
                    "The enabled test process must issue its disclosure on stderr.");

                using JsonDocument identity = JsonDocument.Parse(await File.ReadAllTextAsync(configPath + ".dab-telemetry.json", deadline.Token));
                Guid apiId = identity.RootElement.GetProperty("apiId").GetGuid();
                Assert.AreNotEqual(Guid.Empty, apiId);
                string receipt = JsonSerializer.Serialize(new
                {
                    startedUtc,
                    completedUtc = DateTimeOffset.UtcNow,
                    apiId,
                    engineSha256,
                    mode = "mcp_stdio",
                    successfulDataRequests = 2,
                    failedDataRequests = 1,
                    expectedLogicalOperations = 3,
                    minimumSuccessfulSqlAttempts = 2,
                    excludedDiscoveryAndControlRequests = 4,
                    customerTelemetryEnabled = false,
                    engineExitCode = process.ExitCode,
                    cloudReceiptVerified = false
                }, new JsonSerializerOptions { WriteIndented = true });
                string receiptPath = IOPath.Combine(TestContext.TestRunResultsDirectory!, "engine-telemetry-cloud-smoke.json");
                await File.WriteAllTextAsync(receiptPath, receipt, deadline.Token);
                TestContext.AddResultFile(receiptPath);
                TestContext.WriteLine(receipt);
                TestContext.WriteLine("Serving verified. Reconcile this API identity/run with AppEvents and customEvents before claiming cloud delivery.");
            }
            finally
            {
                bool processExited = !processStarted || process.HasExited;
                try
                {
                    if (!processExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(_timeout);
                        processExited = true;
                    }
                }
                finally
                {
                    // Never disconnect an unrelated database or tear down a running test host.
                    if (processExited)
                    {
                        try
                        {
                            if (databaseCreated && databaseName.StartsWith(DATABASE_PREFIX, StringComparison.Ordinal) &&
                                Guid.TryParseExact(databaseName[DATABASE_PREFIX.Length..], "N", out _))
                            {
                                using CancellationTokenSource cleanup = new(_timeout);
                                await ExecuteSqlAsync("master", $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];", cleanup.Token);
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

        private static async Task<JsonElement> RequestAsync(Process process, int id, string method, object parameters, CancellationToken token)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));
            await process.StandardInput.FlushAsync(token);
            string? line = await process.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(_timeout, token);
            Assert.IsFalse(string.IsNullOrWhiteSpace(line), "The engine exited before its expected protocol response.");
            using JsonDocument reply = JsonDocument.Parse(line!);
            Assert.IsFalse(reply.RootElement.TryGetProperty("error", out _), "The intended handler returned a JSON-RPC error.");
            Assert.AreEqual(id, reply.RootElement.GetProperty("id").GetInt32());
            return reply.RootElement.GetProperty("result").Clone();
        }

        private static JsonElement ToolPayload(JsonElement result, bool isError)
        {
            Assert.AreEqual(isError, result.TryGetProperty("isError", out JsonElement error) && error.GetBoolean());
            JsonElement content = result.GetProperty("content");
            Assert.AreEqual(1, content.GetArrayLength());
            using JsonDocument payload = JsonDocument.Parse(content[0].GetProperty("text").GetString()!);
            return payload.RootElement.Clone();
        }

        private static string CreateConnectionString(string databaseName) => new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = true,
            Pooling = false,
            ConnectTimeout = 15,
            ConnectRetryCount = 0
        }.ConnectionString;

        private static async Task ExecuteSqlAsync(string databaseName, string sql, CancellationToken token)
        {
            await using SqlConnection connection = new(CreateConnectionString(databaseName));
            await connection.OpenAsync(token);
            using SqlCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            command.CommandTimeout = 30;
            await command.ExecuteNonQueryAsync(token);
        }

        private static string CreateConfiguration() => """
            {
              "data-source": { "database-type": "mssql", "connection-string": "@env('DAB_CLOUD_SMOKE_DB')" },
              "runtime": {
                "rest": { "enabled": true, "path": "/api" },
                "graphql": { "enabled": true, "path": "/graphql", "allow-introspection": true },
                "mcp": {
                  "enabled": true, "path": "/mcp",
                  "dml-tools": {
                    "describe-entities": true, "read-records": true, "create-record": false,
                    "update-record": false, "delete-record": false, "execute-entity": false, "aggregate-records": false
                  }
                },
                "host": {
                  "mode": "development", "authentication": { "provider": "Simulator" },
                  "cors": { "origins": [], "allow-credentials": false }
                },
                "cache": { "enabled": false }, "health": { "enabled": false },
                "telemetry": {
                  "application-insights": { "enabled": false }, "open-telemetry": { "enabled": false },
                  "azure-log-analytics": { "enabled": false }, "file": { "enabled": false }
                }
              },
              "entities": {
                "SmokeItems": {
                  "source": { "object": "dbo.CloudSmokeItems", "type": "table" },
                  "mcp": true, "rest": { "enabled": true },
                  "graphql": { "enabled": true, "type": { "singular": "SmokeItem", "plural": "SmokeItems" } },
                  "permissions": [ { "role": "anonymous", "actions": [ "read" ] } ]
                }
              }
            }
            """;
    }
}

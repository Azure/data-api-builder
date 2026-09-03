// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class McpHttpToolRegistryHotReloadIntegrationTests
    {
        private const string MCP_PATH = "/mcp";
        private const string TEST_CONNECTION_STRING_ENV = "DAB_TEST_MSSQL_CONNECTION_STRING";

        [TestMethod]
        public async Task HttpTransport_FileReload_UpdatesDiscoveryCallsAndRecoversFromFailure()
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            SqlConnectionStringBuilder connectionString = new(
                Environment.GetEnvironmentVariable(TEST_CONNECTION_STRING_ENV) ??
                ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL))
            {
                TrustServerCertificate = true
            };
            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                $"dab-mcp-hot-reload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDirectory);
            string configPath = Path.Combine(testDirectory, "dab-config.json");
            await WriteConfigAsync(
                configPath,
                CreateConfig(connectionString.ConnectionString, ("GetBook", "Initial description")));

            try
            {
                string[] args =
                {
                    $"--ConfigFileName={configPath}",
                    "--no-https-redirect"
                };
                using RejectedCandidateLogObserver rejectedCandidateLogObserver = new();
                using TestServer server = new(
                    Program.CreateWebHostBuilder(args)
                        .ConfigureLogging(logging =>
                            logging.AddProvider(rejectedCandidateLogObserver)));
                using HttpClient client = server.CreateClient();

                McpHttpResponse initialize = await SendMcpAsync(
                    client,
                    sessionId: null,
                    new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        method = "initialize",
                        @params = new
                        {
                            protocolVersion = "2025-11-25",
                            capabilities = new { },
                            clientInfo = new { name = "hot-reload-test", version = "1.0" }
                        }
                    },
                    HttpStatusCode.OK);
                Assert.IsNotNull(initialize.SessionId);
                JsonElement toolCapabilities = initialize.Payload!.Value
                    .GetProperty("result")
                    .GetProperty("capabilities")
                    .GetProperty("tools");
                Assert.IsTrue(
                    !toolCapabilities.TryGetProperty("listChanged", out JsonElement listChanged) ||
                    !listChanged.GetBoolean(),
                    "HTTP must not advertise listChanged until session broadcast is implemented.");

                string sessionId = initialize.SessionId;
                await SendMcpAsync(
                    client,
                    sessionId,
                    new
                    {
                        jsonrpc = "2.0",
                        method = "notifications/initialized",
                        @params = new { }
                    },
                    HttpStatusCode.Accepted);

                JsonElement initialList = await ListToolsAsync(client, sessionId, requestId: 2);
                AssertTool(initialList, "get_book", "Initial description");
                Assert.IsTrue(
                    GetTools(initialList)
                        .Single(tool => tool.GetProperty("name").GetString() == "get_book")
                        .GetProperty("inputSchema")
                        .GetProperty("properties")
                        .TryGetProperty("id", out JsonElement idSchema) &&
                    idSchema.GetProperty("type").GetString() == "integer",
                    "Initial HTTP discovery should use database metadata.");
                await AssertToolCallSucceedsAsync(client, sessionId, "get_book", requestId: 3);

                // Change only the backing stored procedure. The refreshed metadata provider must
                // supply update_book_title's additional @title parameter to the new tool schema.
                await WriteConfigAsync(
                    configPath,
                    CreateConfig(
                        connectionString.ConnectionString,
                        storedProcedure: "update_book_title",
                        dmlToolsEnabled: false,
                        ("GetBook", "Initial description")));
                JsonElement changedSchemaList = await WaitForToolSchemaPropertyAsync(
                    client,
                    sessionId,
                    toolName: "get_book",
                    propertyName: "title");
                JsonElement changedProperties = GetTools(changedSchemaList)
                    .Single(tool => tool.GetProperty("name").GetString() == "get_book")
                    .GetProperty("inputSchema")
                    .GetProperty("properties");
                Assert.AreEqual("integer", changedProperties.GetProperty("id").GetProperty("type").GetString());
                Assert.AreEqual("string", changedProperties.GetProperty("title").GetProperty("type").GetString());

                // Global built-in DML visibility is also snapshot state. Toggle it through real
                // file changes and observe the production HTTP tools/list handler in both directions.
                await WriteConfigAsync(
                    configPath,
                    CreateConfig(
                        connectionString.ConnectionString,
                        storedProcedure: "update_book_title",
                        dmlToolsEnabled: true,
                        ("GetBook", "Initial description")));
                JsonElement dmlEnabledList = await WaitForToolSetAsync(
                    client,
                    sessionId,
                    expectedName: "create_record",
                    absentName: "not_a_tool");
                Assert.IsTrue(HasTool(dmlEnabledList, "get_book"));

                await WriteConfigAsync(
                    configPath,
                    CreateConfig(
                        connectionString.ConnectionString,
                        storedProcedure: "update_book_title",
                        dmlToolsEnabled: false,
                        ("GetBook", "Initial description")));
                JsonElement dmlDisabledList = await WaitForToolSetAsync(
                    client,
                    sessionId,
                    expectedName: "get_book",
                    absentName: "create_record");
                Assert.IsFalse(HasTool(dmlDisabledList, "create_record"));

                await WriteConfigAsync(
                    configPath,
                    CreateConfig(connectionString.ConnectionString, ("LookupBook", "Reloaded description")));
                JsonElement renamedList = await WaitForToolSetAsync(
                    client,
                    sessionId,
                    expectedName: "lookup_book",
                    absentName: "get_book");
                AssertTool(renamedList, "lookup_book", "Reloaded description");
                await AssertToolCallFailsAsync(client, sessionId, "get_book", requestId: 4);
                await AssertToolCallSucceedsAsync(client, sessionId, "lookup_book", requestId: 5);

                // Two physical writes without waiting for the first reload to finish exercise
                // coalesced/overlapping watcher notifications. The eventual snapshot must be the
                // latest complete generation.
                await WriteConfigAsync(
                    configPath,
                    CreateConfig(connectionString.ConnectionString, ("IntermediateBook", "Intermediate")));
                await Task.Delay(20);
                await WriteConfigAsync(
                    configPath,
                    CreateConfig(connectionString.ConnectionString, ("LatestBook", "Latest")));
                JsonElement latestList = await WaitForToolSetAsync(
                    client,
                    sessionId,
                    expectedName: "latest_book",
                    absentName: "lookup_book");
                AssertTool(latestList, "latest_book", "Latest");

                // Both entity names normalize to duplicate_tool. The rejected candidate must leave
                // latest_book published until a later valid file change recovers.
                await WriteConfigAsync(
                    configPath,
                    CreateConfig(
                        connectionString.ConnectionString,
                        ("DuplicateTool", "First duplicate"),
                        ("duplicate_tool", "Second duplicate")));
                await rejectedCandidateLogObserver.WaitForRejectionAsync(
                    TimeSpan.FromSeconds(10));

                JsonElement afterFailure = await ListToolsAsync(client, sessionId, requestId: 6);
                AssertTool(afterFailure, "latest_book", "Latest");
                Assert.IsFalse(HasTool(afterFailure, "duplicate_tool"));

                await WriteConfigAsync(
                    configPath,
                    CreateConfig(connectionString.ConnectionString, ("RecoveredBook", "Recovered")));
                JsonElement recoveredList = await WaitForToolSetAsync(
                    client,
                    sessionId,
                    expectedName: "recovered_book",
                    absentName: "latest_book");
                AssertTool(recoveredList, "recovered_book", "Recovered");
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        private static RuntimeConfig CreateConfig(
            string connectionString,
            params (string EntityName, string Description)[] tools)
        {
            return CreateConfig(
                connectionString,
                storedProcedure: "get_book_by_id",
                dmlToolsEnabled: false,
                tools);
        }

        private static RuntimeConfig CreateConfig(
            string connectionString,
            string storedProcedure,
            bool dmlToolsEnabled,
            params (string EntityName, string Description)[] tools)
        {
            Dictionary<string, Entity> entities = tools.ToDictionary(
                tool => tool.EntityName,
                tool => new Entity(
                    Source: new(
                        Object: storedProcedure,
                        Type: EntitySourceType.StoredProcedure,
                        Parameters: null,
                        KeyFields: null),
                    GraphQL: new(
                        Singular: tool.EntityName,
                        Plural: tool.EntityName,
                        Enabled: false,
                        Operation: GraphQLOperation.Mutation),
                    Rest: new(Enabled: true),
                    Fields: null,
                    Permissions: new[]
                    {
                        new EntityPermission(
                            Role: AuthorizationResolver.ROLE_ANONYMOUS,
                            Actions: new[]
                            {
                                new EntityAction(
                                    Action: EntityActionOperation.Execute,
                                    Fields: null,
                                    Policy: null)
                            })
                    },
                    Relationships: null,
                    Mappings: null,
                    Description: tool.Description,
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false)));

            return new RuntimeConfig(
                Schema: FileSystemRuntimeConfigLoader.SCHEMA,
                DataSource: new DataSource(DatabaseType.MSSQL, connectionString, Options: null),
                Runtime: new(
                    Rest: new(Enabled: true),
                    GraphQL: new(Enabled: false),
                    Mcp: new(
                        Enabled: true,
                        Path: MCP_PATH,
                        DmlTools: DmlToolsConfig.FromBoolean(dmlToolsEnabled)),
                    Host: new(
                        Cors: null,
                        Authentication: new(
                            Provider: AuthenticationOptions.UNAUTHENTICATED_AUTHENTICATION),
                        Mode: HostMode.Development)),
                Entities: new(entities));
        }

        private static async Task WriteConfigAsync(string configPath, RuntimeConfig config)
        {
            const int MAX_ATTEMPTS = 20;
            for (int attempt = 1; attempt <= MAX_ATTEMPTS; attempt++)
            {
                try
                {
                    await File.WriteAllTextAsync(configPath, config.ToJson());
                    return;
                }
                catch (IOException) when (attempt < MAX_ATTEMPTS)
                {
                    await Task.Delay(25);
                }
            }
        }

        private static async Task<McpHttpResponse> SendMcpAsync(
            HttpClient client,
            string? sessionId,
            object payload,
            HttpStatusCode expectedStatus)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, MCP_PATH)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("Accept", "application/json, text/event-stream");
            if (sessionId is not null)
            {
                request.Headers.Add("Mcp-Session-Id", sessionId);
            }

            using HttpResponseMessage response = await client.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expectedStatus, response.StatusCode, responseBody);

            string? responseSessionId = response.Headers.TryGetValues(
                "Mcp-Session-Id",
                out IEnumerable<string>? values)
                ? values.Single()
                : sessionId;
            JsonElement? responsePayload = string.IsNullOrWhiteSpace(responseBody)
                ? null
                : ParseMcpPayload(responseBody);
            return new McpHttpResponse(responseSessionId, responsePayload);
        }

        private static async Task<JsonElement> ListToolsAsync(
            HttpClient client,
            string sessionId,
            int requestId)
        {
            McpHttpResponse response = await SendMcpAsync(
                client,
                sessionId,
                new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/list",
                    @params = new { }
                },
                HttpStatusCode.OK);
            return response.Payload!.Value;
        }

        private static async Task<JsonElement> WaitForToolSetAsync(
            HttpClient client,
            string sessionId,
            string expectedName,
            string absentName)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                JsonElement response = await ListToolsAsync(client, sessionId, 100 + attempt);
                if (HasTool(response, expectedName) && !HasTool(response, absentName))
                {
                    return response;
                }

                await Task.Delay(100);
            }

            Assert.Fail($"Timed out waiting for MCP tool '{expectedName}' to replace '{absentName}'.");
            return default;
        }

        private static async Task<JsonElement> WaitForToolSchemaPropertyAsync(
            HttpClient client,
            string sessionId,
            string toolName,
            string propertyName)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                JsonElement response = await ListToolsAsync(client, sessionId, 300 + attempt);
                JsonElement? matchingTool = GetTools(response)
                    .Cast<JsonElement?>()
                    .SingleOrDefault(tool =>
                        tool?.GetProperty("name").GetString() == toolName);
                if (matchingTool.HasValue &&
                    matchingTool.Value
                        .GetProperty("inputSchema")
                        .GetProperty("properties")
                        .TryGetProperty(propertyName, out _))
                {
                    return response;
                }

                await Task.Delay(100);
            }

            Assert.Fail(
                $"Timed out waiting for MCP tool '{toolName}' schema property '{propertyName}'.");
            return default;
        }

        private static async Task AssertToolCallSucceedsAsync(
            HttpClient client,
            string sessionId,
            string toolName,
            int requestId)
        {
            McpHttpResponse response = await SendMcpAsync(
                client,
                sessionId,
                new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/call",
                    @params = new
                    {
                        name = toolName,
                        arguments = new { id = 1 }
                    }
                },
                HttpStatusCode.OK);

            Assert.IsTrue(response.Payload!.Value.TryGetProperty("result", out JsonElement result));
            Assert.IsFalse(result.TryGetProperty("isError", out JsonElement isError) && isError.GetBoolean());
        }

        private static async Task AssertToolCallFailsAsync(
            HttpClient client,
            string sessionId,
            string toolName,
            int requestId)
        {
            McpHttpResponse response = await SendMcpAsync(
                client,
                sessionId,
                new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/call",
                    @params = new
                    {
                        name = toolName,
                        arguments = new { id = 1 }
                    }
                },
                HttpStatusCode.OK);

            JsonElement payload = response.Payload!.Value;
            bool hasJsonRpcError = payload.TryGetProperty("error", out _);
            bool hasToolError = payload.TryGetProperty("result", out JsonElement result) &&
                result.TryGetProperty("isError", out JsonElement isError) &&
                isError.GetBoolean();
            Assert.IsTrue(
                hasJsonRpcError || hasToolError,
                $"Calling removed tool '{toolName}' should return an MCP error result.");
        }

        private static JsonElement ParseMcpPayload(string responseBody)
        {
            string json = responseBody.TrimStart().StartsWith('{')
                ? responseBody
                : responseBody
                    .Split('\n')
                    .Select(line => line.TrimEnd('\r'))
                    .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                    .Select(line => line["data:".Length..].TrimStart())
                    .First(payload => payload.StartsWith('{'));
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static IEnumerable<JsonElement> GetTools(JsonElement response)
        {
            return response
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray();
        }

        private static bool HasTool(JsonElement response, string name)
        {
            return GetTools(response)
                .Any(tool => string.Equals(
                    tool.GetProperty("name").GetString(),
                    name,
                    StringComparison.Ordinal));
        }

        private static void AssertTool(JsonElement response, string name, string description)
        {
            JsonElement tool = GetTools(response)
                .Single(tool => tool.GetProperty("name").GetString() == name);
            Assert.AreEqual(description, tool.GetProperty("description").GetString());
        }

        private sealed record McpHttpResponse(string? SessionId, JsonElement? Payload);

        private sealed class RejectedCandidateLogObserver : ILoggerProvider
        {
            private readonly TaskCompletionSource _rejectionObserved = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public ILogger CreateLogger(string categoryName)
            {
                return new RejectedCandidateLogger(_rejectionObserved);
            }

            public async Task WaitForRejectionAsync(TimeSpan timeout)
            {
                await _rejectionObserved.Task.WaitAsync(timeout);
            }

            public void Dispose()
            {
            }

            private sealed class RejectedCandidateLogger(
                TaskCompletionSource rejectionObserved) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state)
                    where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    if (logLevel == LogLevel.Error &&
                        formatter(state, exception).Contains(
                            "Failed to refresh the MCP tool registry after a runtime configuration change.",
                            StringComparison.Ordinal))
                    {
                        rejectionObserved.TrySetResult();
                    }
                }
            }
        }
    }
}

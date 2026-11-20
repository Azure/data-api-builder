// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpServerConfigurationTests
    {
        [TestMethod]
        public void ConfigureMcpServer_HttpDoesNotAdvertiseToolListChanges()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.ConfigureMcpServer(instructions: null);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            McpServerOptions options = serviceProvider
                .GetRequiredService<IOptions<McpServerOptions>>()
                .Value;

            Assert.IsNotNull(options.Capabilities);
            Assert.IsNotNull(options.Capabilities.Tools);
            Assert.IsFalse(
                options.Capabilities.Tools.ListChanged,
                "HTTP must not promise tool-list notifications until session broadcast is implemented.");
        }

        [TestMethod]
        public async Task ListToolsHandler_RegistrySnapshot_OmitsDisabledTool()
        {
            McpToolRegistry registry = new();
            RuntimeConfig config = CreateRuntimeConfig();
            registry.ReplaceAll(new[] { new DisabledMcpTool() }, config);

#pragma warning disable ASPDEPR004 // TestServer uses the legacy in-memory web-host builder.
            IWebHostBuilder hostBuilder = new WebHostBuilder()
#pragma warning restore ASPDEPR004
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(registry);
                    services.ConfigureMcpServer(instructions: null);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapMcp("/mcp"));
                });
            using TestServer server = new(hostBuilder);
            using HttpClient client = server.CreateClient();

            using HttpRequestMessage initializeRequest = CreateRequest(
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
                        clientInfo = new { name = "registration-test", version = "1.0" }
                    }
                });
            using HttpResponseMessage initializeResponse = await client.SendAsync(initializeRequest);
            Assert.AreEqual(HttpStatusCode.OK, initializeResponse.StatusCode);
            string sessionId = initializeResponse.Headers
                .GetValues("Mcp-Session-Id")
                .Single();

            using HttpRequestMessage initializedRequest = CreateRequest(
                sessionId,
                new
                {
                    jsonrpc = "2.0",
                    method = "notifications/initialized",
                    @params = new { }
                });
            using HttpResponseMessage initializedResponse = await client.SendAsync(initializedRequest);
            Assert.AreEqual(HttpStatusCode.Accepted, initializedResponse.StatusCode);

            using HttpRequestMessage listRequest = CreateRequest(
                sessionId,
                new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "tools/list",
                    @params = new { }
                });
            using HttpResponseMessage listResponse = await client.SendAsync(listRequest);
            string responseBody = await listResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode, responseBody);
            using JsonDocument payload = JsonDocument.Parse(GetJsonPayload(responseBody));

            Assert.AreEqual(
                0,
                payload.RootElement
                    .GetProperty("result")
                    .GetProperty("tools")
                    .GetArrayLength(),
                "The HTTP handler must serve the configuration-aware advertised snapshot.");
            Assert.IsTrue(
                registry.TryGetTool("disabled_tool", out _),
                "Disabled tools remain registered for structured execution-time errors.");
        }

        private static HttpRequestMessage CreateRequest(string? sessionId, object payload)
        {
            HttpRequestMessage request = new(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("Accept", "application/json, text/event-stream");
            if (sessionId is not null)
            {
                request.Headers.Add("Mcp-Session-Id", sessionId);
            }

            return request;
        }

        private static string GetJsonPayload(string responseBody)
        {
            return responseBody.TrimStart().StartsWith('{')
                ? responseBody
                : responseBody
                    .Split('\n')
                    .Select(line => line.TrimEnd('\r'))
                    .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                    .Select(line => line["data:".Length..].TrimStart())
                    .First(payload => payload.StartsWith('{'));
        }

        private static RuntimeConfig CreateRuntimeConfig()
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty, Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity>()));
        }

        private sealed class DisabledMcpTool : IMcpTool
        {
            public ToolType ToolType => ToolType.Custom;

            public Tool GetToolMetadata()
            {
                return new Tool
                {
                    Name = "disabled_tool",
                    Description = "Disabled test tool",
                    InputSchema = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\"}")
                };
            }

            public bool IsEnabled(RuntimeConfig config) => false;

            public Task<CallToolResult> ExecuteAsync(
                JsonDocument? arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }
    }
}

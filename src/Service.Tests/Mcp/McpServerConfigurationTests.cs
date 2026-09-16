// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class McpServerConfigurationTests
    {
        /// <summary>
        /// Verifies server identity and tool capabilities are always configured while blank instructions are omitted.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, null, DisplayName = "Null instructions are omitted")]
        [DataRow("   ", null, DisplayName = "Whitespace instructions are omitted")]
        [DataRow("Use discovered tools only.", "Use discovered tools only.", DisplayName = "Nonblank instructions are preserved")]
        public void ConfigureMcpServer_ConfiguresServerOptions(string? instructions, string? expectedInstructions)
        {
            ServiceCollection services = new();

            IServiceProvider provider = services.ConfigureMcpServer(instructions).BuildServiceProvider();
            McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

            Assert.AreEqual(McpProtocolDefaults.MCP_SERVER_NAME, options.ServerInfo!.Name);
            Assert.AreEqual(McpProtocolDefaults.MCP_SERVER_VERSION, options.ServerInfo.Version);
            Assert.IsNotNull(options.Capabilities);
            Assert.IsNotNull(options.Capabilities.Tools);
            Assert.AreEqual(expectedInstructions, options.ServerInstructions);
        }

        /// <summary>
        /// Exercises discovery and invocation through the real MCP HTTP handler, with no database or network listener.
        /// Advertised ordering shapes remain different; malformed calls must return actionable tool errors.
        /// </summary>
        [DataTestMethod]
        [DataRow("read_records", "{\"entity\":\"Book\",\"orderby\":\"id desc\"}", "array of non-empty strings")]
        [DataRow("read_records", "{\"entity\":\"Book\",\"orderby\":[\"id desc\",123]}", "array of non-empty strings")]
        [DataRow("aggregate_records", "{\"entity\":\"Book\",\"function\":\"count\",\"groupby\":[\"title\"],\"orderby\":[\"desc\"]}", "string ('asc' or 'desc')")]
        [DataRow("aggregate_records", "{\"entity\":\"Book\",\"function\":\"count\",\"orderby\":[\"desc\"]}", "string ('asc' or 'desc')")]
        public async Task ConfigureMcpServer_InvalidOrderby_ReturnsActionableToolError(string toolName, string json, string expectedShape)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            RuntimeConfig config = new(
                Schema: "test-schema",
                DataSource: new(DatabaseType.MSSQL, ConnectionString: string.Empty, Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: DmlToolsConfig.Default),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity>()));
            builder.Services.AddSingleton(TestHelper.GenerateInMemoryRuntimeConfigProvider(config));
            McpToolRegistry registry = new();
            registry.RegisterTool(new ReadRecordsTool());
            registry.RegisterTool(new AggregateRecordsTool());
            builder.Services.AddSingleton(registry);
            builder.Services.ConfigureMcpServer(instructions: null);
            builder.Services.PostConfigure<HttpServerTransportOptions>(options => options.Stateless = true);

            await using WebApplication app = builder.Build();
            app.MapMcp("/mcp");
            await app.StartAsync();
            using HttpClient client = app.GetTestClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
            client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");

            JsonElement initialized = await PostRpcAsync(client, 1, "initialize", new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "orderby-validation-test", version = "1" }
            });
            Assert.IsTrue(initialized.TryGetProperty("result", out _));

            JsonElement listed = await PostRpcAsync(client, 2, "tools/list", new { });
            JsonElement tools = listed.GetProperty("result").GetProperty("tools");
            Assert.AreEqual(2, tools.GetArrayLength());
            foreach (JsonElement tool in tools.EnumerateArray())
            {
                string expectedType = tool.GetProperty("name").GetString() == "read_records" ? "array" : "string";
                Assert.AreEqual(expectedType, tool.GetProperty("inputSchema").GetProperty("properties")
                    .GetProperty("orderby").GetProperty("type").GetString());
            }

            using JsonDocument arguments = JsonDocument.Parse(json);
            JsonElement response = await PostRpcAsync(client, 3, "tools/call", new { name = toolName, arguments = arguments.RootElement });
            JsonElement result = response.GetProperty("result");
            Assert.IsTrue(result.GetProperty("isError").GetBoolean());
            using JsonDocument content = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
            Assert.AreEqual(toolName, content.RootElement.GetProperty("toolName").GetString());
            JsonElement error = content.RootElement.GetProperty("error");
            Assert.AreEqual("InvalidArguments", error.GetProperty("type").GetString());
            string message = error.GetProperty("message").GetString()!;
            StringAssert.Contains(message, "'orderby'");
            StringAssert.Contains(message, expectedShape);
            StringAssert.Contains(message, "for example");

            await app.StopAsync();
        }

        private static async Task<JsonElement> PostRpcAsync(HttpClient client, int id, string method, object parameters)
        {
            string json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
            using StringContent body = new(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync("/mcp", body);
            string payload = await response.Content.ReadAsStringAsync();
            Assert.IsTrue(response.IsSuccessStatusCode, $"HTTP {response.StatusCode}: {payload}");

            if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                payload = payload.Split('\n').Last(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim();
            }

            using JsonDocument document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
    }
}

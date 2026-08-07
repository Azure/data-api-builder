// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpStdioServerInitializeTests
    {
        [TestMethod]
        public void HandleInitialize_ClientRequests2025_11_25_WithDescription_UsesServerInfoDescription()
        {
            const string DESCRIPTION = "mcp description";
            McpStdioServer server = CreateServer(description: DESCRIPTION, out StringWriter stdoutCapture);

            JsonElement responseRoot = InvokeHandleInitialize(
                server,
                stdoutCapture,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"client","version":"1.0.0"}}}
                """);

            AssertInitializeEnvelopeAndCapabilities(responseRoot, expectedId: 1, expectedProtocolVersion: "2025-11-25");
            JsonElement result = responseRoot.GetProperty("result");

            Assert.IsTrue(result.TryGetProperty("serverInfo", out JsonElement serverInfo), "Expected result.serverInfo.");
            Assert.AreEqual(DESCRIPTION, serverInfo.GetProperty("description").GetString());
            Assert.IsFalse(result.TryGetProperty("instructions", out _), "Did not expect top-level instructions for 2025-11-25.");
            Assert.AreEqual(1, CountOutputLines(stdoutCapture));
        }

        [TestMethod]
        public void HandleInitialize_ClientRequests2025_06_18_WithDescription_UsesTopLevelInstructions()
        {
            const string DESCRIPTION = "legacy instruction text";
            McpStdioServer server = CreateServer(description: DESCRIPTION, out StringWriter stdoutCapture);

            JsonElement responseRoot = InvokeHandleInitialize(
                server,
                stdoutCapture,
                """
                {"jsonrpc":"2.0","id":"abc","method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"client","version":"1.0.0"}}}
                """);

            AssertInitializeEnvelopeAndCapabilities(responseRoot, expectedId: "abc", expectedProtocolVersion: "2025-06-18");
            JsonElement result = responseRoot.GetProperty("result");

            Assert.AreEqual(DESCRIPTION, result.GetProperty("instructions").GetString());
            Assert.IsFalse(result.GetProperty("serverInfo").TryGetProperty("description", out _), "Did not expect serverInfo.description for 2025-06-18.");
            Assert.AreEqual(1, CountOutputLines(stdoutCapture));
        }

        [TestMethod]
        public void HandleInitialize_ClientRequests2025_11_25_WithoutDescription_EmitsNeitherField()
        {
            McpStdioServer server = CreateServer(description: null, out StringWriter stdoutCapture);

            JsonElement responseRoot = InvokeHandleInitialize(
                server,
                stdoutCapture,
                """
                {"jsonrpc":"2.0","id":2,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"client","version":"1.0.0"}}}
                """);

            AssertInitializeEnvelopeAndCapabilities(responseRoot, expectedId: 2, expectedProtocolVersion: "2025-11-25");
            JsonElement result = responseRoot.GetProperty("result");

            Assert.IsFalse(result.TryGetProperty("instructions", out _), "Did not expect top-level instructions when description is not configured.");
            Assert.IsFalse(result.GetProperty("serverInfo").TryGetProperty("description", out _), "Did not expect serverInfo.description when description is not configured.");
            Assert.AreEqual(1, CountOutputLines(stdoutCapture));
        }

        private static McpStdioServer CreateServer(string? description, out StringWriter stdoutCapture)
        {
            stdoutCapture = new StringWriter();
            McpStdoutWriter stdoutWriter = new(stdoutCapture);

            RuntimeConfig runtimeConfig = new(
                Schema: RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK,
                DataSource: null,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                Runtime: new RuntimeOptions(
                    Rest: null,
                    GraphQL: null,
                    Mcp: new McpRuntimeOptions(Description: description),
                    Host: null));
            RuntimeConfigProvider runtimeConfigProvider = new StubRuntimeConfigProvider(runtimeConfig);

            IConfiguration configuration = new ConfigurationBuilder().Build();
            ServiceProvider serviceProvider = new ServiceCollection()
                .AddSingleton(configuration)
                .AddSingleton(stdoutWriter)
                .AddSingleton(runtimeConfigProvider)
                .BuildServiceProvider();

            return new McpStdioServer(new McpToolRegistry(), serviceProvider);
        }

        private static JsonElement InvokeHandleInitialize(McpStdioServer server, StringWriter stdoutCapture, string initializeRequestJson)
        {
            MethodInfo? handleInitialize = typeof(McpStdioServer).GetMethod("HandleInitialize", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(handleInitialize, "Expected private HandleInitialize method to exist.");

            using JsonDocument request = JsonDocument.Parse(initializeRequestJson);
            JsonElement requestRoot = request.RootElement;
            JsonElement? id = requestRoot.TryGetProperty("id", out JsonElement idElement) ? idElement : null;

            handleInitialize.Invoke(server, new object?[] { id, requestRoot });

            string output = ExtractSingleOutputLine(stdoutCapture);
            using JsonDocument response = JsonDocument.Parse(output);
            return response.RootElement.Clone();
        }

        private static void AssertInitializeEnvelopeAndCapabilities(JsonElement responseRoot, object expectedId, string expectedProtocolVersion)
        {
            Assert.AreEqual("2.0", responseRoot.GetProperty("jsonrpc").GetString());
            if (expectedId is int expectedNumericId)
            {
                Assert.AreEqual(expectedNumericId, responseRoot.GetProperty("id").GetInt32());
            }
            else
            {
                Assert.AreEqual(expectedId, responseRoot.GetProperty("id").GetString());
            }

            JsonElement result = responseRoot.GetProperty("result");
            Assert.AreEqual(expectedProtocolVersion, result.GetProperty("protocolVersion").GetString());

            JsonElement capabilities = result.GetProperty("capabilities");
            Assert.IsTrue(capabilities.GetProperty("tools").GetProperty("listChanged").GetBoolean());
            Assert.AreEqual(JsonValueKind.Object, capabilities.GetProperty("logging").ValueKind);

            JsonElement serverInfo = result.GetProperty("serverInfo");
            Assert.AreEqual(McpProtocolDefaults.MCP_SERVER_NAME, serverInfo.GetProperty("name").GetString());
            Assert.AreEqual(McpProtocolDefaults.MCP_SERVER_VERSION, serverInfo.GetProperty("version").GetString());
        }

        private static int CountOutputLines(StringWriter stdoutCapture)
        {
            return stdoutCapture
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }

        private static string ExtractSingleOutputLine(StringWriter stdoutCapture)
        {
            string[] lines = stdoutCapture
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(1, lines.Length, "Expected a single JSON-RPC response line.");
            return lines[0];
        }

        private sealed class StubRuntimeConfigProvider : RuntimeConfigProvider
        {
            private readonly RuntimeConfig _runtimeConfig;

            public StubRuntimeConfigProvider(RuntimeConfig runtimeConfig) : base(new StubRuntimeConfigLoader())
            {
                _runtimeConfig = runtimeConfig;
            }

            public override RuntimeConfig GetConfig()
            {
                return _runtimeConfig;
            }
        }

        private sealed class StubRuntimeConfigLoader : RuntimeConfigLoader
        {
            public override bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config, bool replaceEnvVar = false)
            {
                config = null;
                return false;
            }

            public override string GetPublishedDraftSchemaLink()
            {
                return RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK;
            }
        }
    }
}

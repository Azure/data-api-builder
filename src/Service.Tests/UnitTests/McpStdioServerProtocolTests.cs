// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpStdioServerProtocolTests
    {
        [TestMethod]
        public async Task RunAsync_OversizedRequest_ReturnsInvalidRequestAndContinues()
        {
            string input = new('x', (1024 * 1024) + 1);
            input += Environment.NewLine + Request(id: 2, method: "ping") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input);

            await server.RunAsync(CancellationToken.None);

            JsonElement[] responses = ParseResponses(output);
            Assert.AreEqual(2, responses.Length);
            AssertError(responses[0], expectedId: null, McpStdioJsonRpcErrorCodes.INVALID_REQUEST, "Request too large");
            Assert.IsTrue(responses[1].GetProperty("result").GetProperty("ok").GetBoolean());
        }

        [TestMethod]
        public async Task RunAsync_MalformedJson_ReturnsParseErrorAndContinues()
        {
            string input = "{not-json" + Environment.NewLine + Request(id: 2, method: "ping") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input);

            await server.RunAsync(CancellationToken.None);

            JsonElement[] responses = ParseResponses(output);
            Assert.AreEqual(2, responses.Length);
            AssertError(responses[0], expectedId: null, McpStdioJsonRpcErrorCodes.PARSE_ERROR, "Parse error");
            Assert.AreEqual(2, responses[1].GetProperty("id").GetInt32());
        }

        [TestMethod]
        public async Task RunAsync_MissingMethod_PreservesStringIdInInvalidRequest()
        {
            (McpStdioServer server, StringWriter output, _) = CreateServer(
                "{\"jsonrpc\":\"2.0\",\"id\":\"request-id\"}" + Environment.NewLine);

            await server.RunAsync(CancellationToken.None);

            AssertError(ParseResponses(output).Single(), "request-id", McpStdioJsonRpcErrorCodes.INVALID_REQUEST, "Invalid Request");
        }

        [TestMethod]
        public async Task RunAsync_InitializedNotificationProducesNoResponse()
        {
            string input = Request(id: null, method: "notifications/initialized") + Environment.NewLine
                + Request(id: 7, method: "shutdown") + Environment.NewLine
                + Request(id: 8, method: "ping") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input);

            await server.RunAsync(CancellationToken.None);

            JsonElement[] responses = ParseResponses(output);
            Assert.AreEqual(1, responses.Length);
            Assert.AreEqual(7, responses[0].GetProperty("id").GetInt32());
            Assert.IsTrue(responses[0].GetProperty("result").GetProperty("ok").GetBoolean());
        }

        [TestMethod]
        public async Task RunAsync_UnknownMethodReturnsMethodNotFoundThenProcessesPing()
        {
            string input = Request(id: 1, method: "unknown/method") + Environment.NewLine
                + Request(id: 2, method: "ping") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input);

            await server.RunAsync(CancellationToken.None);

            JsonElement[] responses = ParseResponses(output);
            Assert.AreEqual(2, responses.Length);
            AssertError(responses[0], 1L, McpStdioJsonRpcErrorCodes.METHOD_NOT_FOUND, "Method not found: unknown/method");
            Assert.IsTrue(responses[1].GetProperty("result").GetProperty("ok").GetBoolean());
        }

        [TestMethod]
        public async Task RunAsync_InitializeConfigurationFailureReturnsInternalError()
        {
            RuntimeConfigProvider provider = new ThrowingRuntimeConfigProvider();
            string input = Request(id: 1, method: "initialize", @params: "{}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input, runtimeConfigProvider: provider);

            await server.RunAsync(CancellationToken.None);

            AssertError(ParseResponses(output).Single(), 1L, McpStdioJsonRpcErrorCodes.INTERNAL_ERROR, "Internal error");
        }

        [TestMethod]
        public async Task RunAsync_ListToolsReturnsOnlyEnabledToolMetadata()
        {
            McpToolRegistry registry = new();
            registry.RegisterTool(new RecordingTool("enabled", isEnabled: true));
            registry.RegisterTool(new RecordingTool("disabled", isEnabled: false));
            string input = Request(id: 4, method: "tools/list") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input, registry: registry);

            await server.RunAsync(CancellationToken.None);

            JsonElement tools = ParseResponses(output).Single().GetProperty("result").GetProperty("tools");
            Assert.AreEqual(1, tools.GetArrayLength());
            Assert.AreEqual("enabled", tools[0].GetProperty("name").GetString());
            Assert.AreEqual("Test tool enabled", tools[0].GetProperty("description").GetString());
            Assert.AreEqual("object", tools[0].GetProperty("inputSchema").GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task RunAsync_ListToolsConfigurationFailureReturnsInternalError()
        {
            string input = Request(id: 3, method: "tools/list") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(
                input,
                runtimeConfigProvider: new ThrowingRuntimeConfigProvider());

            await server.RunAsync(CancellationToken.None);

            AssertError(ParseResponses(output).Single(), 3L, McpStdioJsonRpcErrorCodes.INTERNAL_ERROR, "Internal error");
        }

        [DataTestMethod]
        [DataRow(null, "Missing params", DisplayName = "Missing params")]
        [DataRow("[]", "Missing params", DisplayName = "Params is an array")]
        [DataRow("{}", "Missing tool name", DisplayName = "Missing name")]
        [DataRow("{\"name\":null}", "Missing tool name", DisplayName = "Null name")]
        [DataRow("{\"name\":\"   \"}", "Missing tool name", DisplayName = "Whitespace name")]
        [DataRow("{\"name\":\"missing\"}", "Tool not found: missing", DisplayName = "Unknown tool")]
        public async Task RunAsync_CallToolRejectsInvalidParameters(string? parameters, string expectedMessage)
        {
            string input = Request(id: 11, method: "tools/call", @params: parameters) + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input);

            await server.RunAsync(CancellationToken.None);

            AssertError(ParseResponses(output).Single(), 11L, McpStdioJsonRpcErrorCodes.INVALID_PARAMS, expectedMessage);
        }

        [TestMethod]
        public async Task RunAsync_CallToolSupportsNameAndArguments()
        {
            RecordingTool tool = new("test_tool");
            McpToolRegistry registry = new();
            registry.RegisterTool(tool);
            string input = Request(
                id: 12,
                method: "tools/call",
                @params: "{\"name\":\"test_tool\",\"arguments\":{\"value\":42}}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input, registry: registry);

            await server.RunAsync(CancellationToken.None);

            Assert.AreEqual("{\"value\":42}", tool.ArgumentsJson);
            JsonElement result = ParseResponses(output).Single().GetProperty("result");
            Assert.AreEqual("executed test_tool", result.GetProperty("content")[0].GetProperty("text").GetString());
            Assert.IsFalse(result.TryGetProperty("isError", out _));
        }

        [TestMethod]
        public async Task RunAsync_CallToolSupportsLegacyToolNameAndMissingArguments()
        {
            RecordingTool tool = new("legacy_tool");
            McpToolRegistry registry = new();
            registry.RegisterTool(tool);
            string input = Request(
                id: 13,
                method: "tools/call",
                @params: "{\"tool\":\"legacy_tool\"}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input, registry: registry);

            await server.RunAsync(CancellationToken.None);

            Assert.IsTrue(tool.Executed);
            Assert.IsNull(tool.ArgumentsJson);
            Assert.AreEqual(13, ParseResponses(output).Single().GetProperty("id").GetInt32());
        }

        [TestMethod]
        public async Task RunAsync_CallToolPrefersStandardNameOverLegacyToolName()
        {
            RecordingTool standardTool = new("standard");
            RecordingTool legacyTool = new("legacy");
            McpToolRegistry registry = new();
            registry.RegisterTool(standardTool);
            registry.RegisterTool(legacyTool);
            string input = Request(
                id: 14,
                method: "tools/call",
                @params: "{\"name\":\"standard\",\"tool\":\"legacy\",\"arguments\":{}}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input, registry: registry);

            await server.RunAsync(CancellationToken.None);

            Assert.IsTrue(standardTool.Executed);
            Assert.IsFalse(legacyTool.Executed);
            Assert.AreEqual(1, ParseResponses(output).Length);
        }

        [TestMethod]
        public async Task RunAsync_CallToolWithConfiguredRoleProvidesScopedIdentityAndClearsAccessor()
        {
            RecordingTool tool = new("role_tool");
            McpToolRegistry registry = new();
            registry.RegisterTool(tool);
            HttpContextAccessor accessor = new();
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["MCP:Role"] = "writer" })
                .Build();
            string input = Request(
                id: 15,
                method: "tools/call",
                @params: "{\"name\":\"role_tool\",\"arguments\":{}}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(
                input,
                registry: registry,
                configuration: configuration,
                httpContextAccessor: accessor);

            await server.RunAsync(CancellationToken.None);

            Assert.AreEqual("writer", tool.ObservedRoleHeader);
            Assert.AreEqual("writer", tool.ObservedRoleClaim);
            Assert.IsNotNull(tool.ObservedServiceProvider);
            Assert.IsNull(accessor.HttpContext, "The request context must be cleared after tool execution.");
            Assert.AreEqual(1, ParseResponses(output).Length);
        }

        [TestMethod]
        public async Task RunAsync_ThrowingToolReturnsInternalErrorAndClearsRoleContext()
        {
            RecordingTool tool = new("throwing_tool", exception: new InvalidOperationException("failure"));
            McpToolRegistry registry = new();
            registry.RegisterTool(tool);
            HttpContextAccessor accessor = new();
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["MCP:Role"] = "reader" })
                .Build();
            string input = Request(
                id: 16,
                method: "tools/call",
                @params: "{\"name\":\"throwing_tool\"}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(
                input,
                registry: registry,
                configuration: configuration,
                httpContextAccessor: accessor);

            await server.RunAsync(CancellationToken.None);

            Assert.IsTrue(tool.Executed);
            Assert.IsNull(accessor.HttpContext);
            AssertError(ParseResponses(output).Single(), 16L, McpStdioJsonRpcErrorCodes.INTERNAL_ERROR, "Internal error");
        }

        [DataTestMethod]
        [DataRow(null, DisplayName = "Missing params")]
        [DataRow("{}", DisplayName = "Missing level")]
        [DataRow("{\"level\":null}", DisplayName = "Null level")]
        [DataRow("{\"level\":\"   \"}", DisplayName = "Whitespace level")]
        public async Task RunAsync_SetLogLevelRejectsMissingOrInvalidLevel(string? parameters)
        {
            string input = Request(id: 21, method: "logging/setLevel", @params: parameters) + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input);

            await server.RunAsync(CancellationToken.None);

            AssertError(
                ParseResponses(output).Single(),
                21L,
                McpStdioJsonRpcErrorCodes.INVALID_PARAMS,
                "Missing or invalid 'level' parameter");
        }

        [TestMethod]
        public async Task RunAsync_SetLogLevelWithoutControllerReturnsSuccess()
        {
            string input = Request(
                id: 22,
                method: "logging/setLevel",
                @params: "{\"level\":\"debug\"}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(input);

            await server.RunAsync(CancellationToken.None);

            Assert.AreEqual(JsonValueKind.Object, ParseResponses(output).Single().GetProperty("result").ValueKind);
        }

        [TestMethod]
        public async Task RunAsync_SetLogLevelInvalidValueHasNoSideEffects()
        {
            RecordingLogLevelController controller = new(updateResult: true);
            RecordingNotificationWriter writer = new() { IsEnabled = false };
            string input = Request(
                id: 23,
                method: "logging/setLevel",
                @params: "{\"level\":\"verbose\"}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(
                input,
                logLevelController: controller,
                notificationWriter: writer);

            await server.RunAsync(CancellationToken.None);

            Assert.IsNull(controller.LastLevel);
            Assert.IsFalse(writer.IsEnabled);
            Assert.AreEqual(JsonValueKind.Object, ParseResponses(output).Single().GetProperty("result").ValueKind);
        }

        [TestMethod]
        public async Task RunAsync_SetLogLevelNoneDisablesNotifications()
        {
            RecordingLogLevelController controller = new(updateResult: false);
            RecordingNotificationWriter writer = new() { IsEnabled = true };
            string input = Request(
                id: 24,
                method: "logging/setLevel",
                @params: "{\"level\":\"NONE\"}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(
                input,
                logLevelController: controller,
                notificationWriter: writer);

            await server.RunAsync(CancellationToken.None);

            Assert.AreEqual("NONE", controller.LastLevel);
            Assert.IsFalse(writer.IsEnabled);
            Assert.AreEqual(1, ParseResponses(output).Length);
        }

        [TestMethod]
        public async Task RunAsync_SetLogLevelValidValueEnablesNotificationsAndRestoresStderr()
        {
            RecordingLogLevelController controller = new(updateResult: true);
            RecordingNotificationWriter writer = new() { IsEnabled = false };
            string input = Request(
                id: 25,
                method: "logging/setLevel",
                @params: "{\"level\":\"warning\"}") + Environment.NewLine;
            (McpStdioServer server, StringWriter output, _) = CreateServer(
                input,
                logLevelController: controller,
                notificationWriter: writer);
            TextWriter originalError = Console.Error;

            try
            {
                Console.SetError(TextWriter.Null);
                await server.RunAsync(CancellationToken.None);

                Assert.AreNotSame(TextWriter.Null, Console.Error);
            }
            finally
            {
                Console.SetError(originalError);
            }

            Assert.AreEqual("warning", controller.LastLevel);
            Assert.IsTrue(writer.IsEnabled);
            Assert.AreEqual(1, ParseResponses(output).Length);
        }

        private static (McpStdioServer Server, StringWriter Output, IServiceProvider Services) CreateServer(
            string input,
            McpToolRegistry? registry = null,
            RuntimeConfigProvider? runtimeConfigProvider = null,
            IConfiguration? configuration = null,
            ILogLevelController? logLevelController = null,
            IMcpLogNotificationWriter? notificationWriter = null,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            registry ??= new McpToolRegistry();
            StringWriter output = new();
            ServiceCollection services = new();
            services.AddSingleton(new McpStdoutWriter(output));
            services.AddSingleton(registry);
            services.AddSingleton(runtimeConfigProvider ?? new StubRuntimeConfigProvider(CreateRuntimeConfig()));
            services.AddSingleton<IConfiguration>(configuration ?? new ConfigurationBuilder().Build());

            if (logLevelController is not null)
            {
                services.AddSingleton(logLevelController);
            }

            if (notificationWriter is not null)
            {
                services.AddSingleton(notificationWriter);
            }

            if (httpContextAccessor is not null)
            {
                services.AddSingleton(httpContextAccessor);
            }

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            McpStdioServer server = new(registry, serviceProvider, new StringReader(input));
            return (server, output, serviceProvider);
        }

        private static string Request(long? id, string method, string? @params = null)
        {
            string idProperty = id.HasValue ? $",\"id\":{id.Value}" : string.Empty;
            string paramsProperty = @params is not null ? $",\"params\":{@params}" : string.Empty;
            return $"{{\"jsonrpc\":\"2.0\"{idProperty},\"method\":\"{method}\"{paramsProperty}}}";
        }

        private static JsonElement[] ParseResponses(StringWriter output)
        {
            return output.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument response = JsonDocument.Parse(line);
                    return response.RootElement.Clone();
                })
                .ToArray();
        }

        private static void AssertError(JsonElement response, object? expectedId, int expectedCode, string expectedMessage)
        {
            Assert.AreEqual(McpStdioJsonRpcErrorCodes.JSON_RPC_VERSION, response.GetProperty("jsonrpc").GetString());
            if (expectedId is null)
            {
                Assert.AreEqual(JsonValueKind.Null, response.GetProperty("id").ValueKind);
            }
            else if (expectedId is string expectedString)
            {
                Assert.AreEqual(expectedString, response.GetProperty("id").GetString());
            }
            else
            {
                Assert.AreEqual(Convert.ToInt64(expectedId), response.GetProperty("id").GetInt64());
            }

            JsonElement error = response.GetProperty("error");
            Assert.AreEqual(expectedCode, error.GetProperty("code").GetInt32());
            Assert.AreEqual(expectedMessage, error.GetProperty("message").GetString());
        }

        private static RuntimeConfig CreateRuntimeConfig()
        {
            return new RuntimeConfig(
                Schema: RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK,
                DataSource: null,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                Runtime: new RuntimeOptions(
                    Rest: null,
                    GraphQL: null,
                    Mcp: new McpRuntimeOptions(Enabled: true),
                    Host: null));
        }

        private sealed class RecordingTool : IMcpTool
        {
            private readonly string _name;
            private readonly bool _isEnabled;
            private readonly Exception? _exception;

            public RecordingTool(string name, bool isEnabled = true, Exception? exception = null)
            {
                _name = name;
                _isEnabled = isEnabled;
                _exception = exception;
            }

            public ToolType ToolType => ToolType.BuiltIn;

            public bool Executed { get; private set; }

            public string? ArgumentsJson { get; private set; }

            public IServiceProvider? ObservedServiceProvider { get; private set; }

            public string? ObservedRoleHeader { get; private set; }

            public string? ObservedRoleClaim { get; private set; }

            public bool IsEnabled(RuntimeConfig config) => _isEnabled;

            public Tool GetToolMetadata()
            {
                using JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\"}");
                return new Tool
                {
                    Name = _name,
                    Description = $"Test tool {_name}",
                    InputSchema = schema.RootElement.Clone()
                };
            }

            public Task<CallToolResult> ExecuteAsync(
                JsonDocument? arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                Executed = true;
                ArgumentsJson = arguments?.RootElement.GetRawText();
                ObservedServiceProvider = serviceProvider;
                IHttpContextAccessor? accessor = serviceProvider.GetService<IHttpContextAccessor>();
                ObservedRoleHeader = accessor?.HttpContext?.Request.Headers["X-MS-API-ROLE"].ToString();
                ObservedRoleClaim = accessor?.HttpContext?.User.FindFirst(ClaimTypes.Role)?.Value;

                if (_exception is not null)
                {
                    throw _exception;
                }

                return Task.FromResult(new CallToolResult
                {
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock { Text = $"executed {_name}" }
                    }
                });
            }
        }

        private sealed class RecordingLogLevelController : ILogLevelController
        {
            private readonly bool _updateResult;

            public RecordingLogLevelController(bool updateResult)
            {
                _updateResult = updateResult;
            }

            public bool IsCliOverriding => false;

            public bool IsConfigOverriding => false;

            public bool IsAgentOverriding => LastLevel is not null;

            public string? LastLevel { get; private set; }

            public bool UpdateFromMcp(string mcpLevel)
            {
                LastLevel = mcpLevel;
                return _updateResult;
            }
        }

        private sealed class RecordingNotificationWriter : IMcpLogNotificationWriter
        {
            public bool IsEnabled { get; set; }

            public void WriteNotification(LogLevel logLevel, string categoryName, string message)
            {
            }
        }

        private sealed class StubRuntimeConfigProvider : RuntimeConfigProvider
        {
            private readonly RuntimeConfig _runtimeConfig;

            public StubRuntimeConfigProvider(RuntimeConfig runtimeConfig)
                : base(new StubRuntimeConfigLoader())
            {
                _runtimeConfig = runtimeConfig;
            }

            public override RuntimeConfig GetConfig() => _runtimeConfig;
        }

        private sealed class ThrowingRuntimeConfigProvider : RuntimeConfigProvider
        {
            public ThrowingRuntimeConfigProvider()
                : base(new StubRuntimeConfigLoader())
            {
            }

            public override RuntimeConfig GetConfig() => throw new InvalidOperationException("Configuration unavailable.");
        }

        private sealed class StubRuntimeConfigLoader : RuntimeConfigLoader
        {
            public override bool TryLoadKnownConfig([NotNullWhen(true)] out RuntimeConfig? config, bool replaceEnvVar = false)
            {
                config = null;
                return false;
            }

            public override string GetPublishedDraftSchemaLink() => RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK;
        }
    }
}

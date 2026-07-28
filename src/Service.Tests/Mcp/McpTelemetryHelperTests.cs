// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <c>McpTelemetryHelper</c> (operation inference, exception→error-code mapping,
    /// and the telemetry execution wrapper). Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class McpTelemetryHelperTests
    {
        [DataTestMethod]
        [DataRow("read_records", "read")]
        [DataRow("create_record", "create")]
        [DataRow("update_record", "update")]
        [DataRow("delete_record", "delete")]
        [DataRow("describe_entities", "describe")]
        [DataRow("execute_entity", "execute")]
        [DataRow("aggregate_records", "aggregate")]
        [DataRow("some_unknown_tool", "execute")]
        public void InferOperationFromTool_BuiltIn_MapsToolNameToOperation(string toolName, string expected)
        {
            FakeTool tool = new(ToolType.BuiltIn);

            Assert.AreEqual(expected, McpTelemetryHelper.InferOperationFromTool(tool, toolName));
        }

        [TestMethod]
        public void InferOperationFromTool_CustomTool_AlwaysReturnsExecute()
        {
            FakeTool tool = new(ToolType.Custom);

            Assert.AreEqual("execute", McpTelemetryHelper.InferOperationFromTool(tool, "anything"));
        }

        [TestMethod]
        public void MapExceptionToErrorCode_MapsKnownExceptionTypes()
        {
            Assert.AreEqual(McpTelemetryErrorCodes.OPERATION_CANCELLED, McpTelemetryHelper.MapExceptionToErrorCode(new OperationCanceledException()));
            Assert.AreEqual(McpTelemetryErrorCodes.OPERATION_TIMEOUT, McpTelemetryHelper.MapExceptionToErrorCode(new TimeoutException()));
            Assert.AreEqual(McpTelemetryErrorCodes.AUTHORIZATION_FAILED, McpTelemetryHelper.MapExceptionToErrorCode(new UnauthorizedAccessException()));
            Assert.AreEqual(McpTelemetryErrorCodes.DATABASE_ERROR, McpTelemetryHelper.MapExceptionToErrorCode(new FakeDbException()));
            Assert.AreEqual(McpTelemetryErrorCodes.INVALID_REQUEST, McpTelemetryHelper.MapExceptionToErrorCode(new ArgumentException("bad")));
            Assert.AreEqual(McpTelemetryErrorCodes.EXECUTION_FAILED, McpTelemetryHelper.MapExceptionToErrorCode(new InvalidOperationException()));
        }

        [TestMethod]
        public void MapExceptionToErrorCode_MapsDataApiBuilderSubStatusCodes()
        {
            DataApiBuilderException authN = new("x", HttpStatusCode.Unauthorized, DataApiBuilderException.SubStatusCodes.AuthenticationChallenge);
            DataApiBuilderException authZ = new("x", HttpStatusCode.Forbidden, DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);

            Assert.AreEqual(McpTelemetryErrorCodes.AUTHENTICATION_FAILED, McpTelemetryHelper.MapExceptionToErrorCode(authN));
            Assert.AreEqual(McpTelemetryErrorCodes.AUTHORIZATION_FAILED, McpTelemetryHelper.MapExceptionToErrorCode(authZ));
        }

        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_SuccessResult_IsReturned()
        {
            CallToolResult success = new()
            {
                Content = new List<ContentBlock> { new TextContentBlock { Text = "{\"status\":\"success\"}" } }
            };
            FakeTool tool = new(ToolType.BuiltIn, success);
            using JsonDocument args = JsonDocument.Parse("{\"entity\":\"Book\"}");

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "read_records", args, EmptyProvider(), CancellationToken.None);

            Assert.IsFalse(result.IsError == true);
        }

        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_ErrorResultWithCodeAndMessage_IsReturned()
        {
            CallToolResult error = new()
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = "{\"code\":\"E1\",\"message\":\"boom\"}" } }
            };
            FakeTool tool = new(ToolType.BuiltIn, error);
            using JsonDocument args = JsonDocument.Parse("{\"entity\":\"Book\"}");

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "create_record", args, EmptyProvider(), CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
        }

        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_ErrorResultNonJsonContent_IsReturned()
        {
            CallToolResult error = new()
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = "not-json" } }
            };
            FakeTool tool = new(ToolType.BuiltIn, error);

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "delete_record", arguments: null, EmptyProvider(), CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
        }

        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_ToolThrows_Rethrows()
        {
            FakeTool tool = new(ToolType.BuiltIn, throwOnExecute: new ArgumentException("bad"));

            await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
                await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                    tool, "update_record", arguments: null, EmptyProvider(), CancellationToken.None));
        }

        private static IServiceProvider EmptyProvider() => new ServiceCollection().BuildServiceProvider();

        private sealed class FakeTool : IMcpTool
        {
            private readonly CallToolResult? _result;
            private readonly Exception? _throw;

            public FakeTool(ToolType toolType, CallToolResult? result = null, Exception? throwOnExecute = null)
            {
                ToolType = toolType;
                _result = result;
                _throw = throwOnExecute;
            }

            public ToolType ToolType { get; }

            public bool IsEnabled(RuntimeConfig config) => true;

            public Tool GetToolMetadata() => new() { Name = "fake" };

            public Task<CallToolResult> ExecuteAsync(JsonDocument? arguments, IServiceProvider serviceProvider, CancellationToken cancellationToken)
            {
                if (_throw is not null)
                {
                    throw _throw;
                }

                return Task.FromResult(_result ?? new CallToolResult());
            }
        }

        private sealed class FakeDbException : DbException
        {
            public FakeDbException() : base("fake db error")
            {
            }

            public FakeDbException(string message) : base(message)
            {
            }

            public FakeDbException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }
    }
}

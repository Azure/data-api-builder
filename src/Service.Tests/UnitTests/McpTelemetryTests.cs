// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Tests for MCP telemetry functionality.
    /// </summary>
    [TestClass]
    public class McpTelemetryTests
    {
        private static ActivityListener? _activityListener;
        private static readonly List<Activity> _recordedActivities = new();

        /// <summary>
        /// Initialize activity listener before all tests.
        /// </summary>
        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = (activitySource) => activitySource.Name == "DataApiBuilder",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity => { },
                ActivityStopped = activity =>
                {
                    _recordedActivities.Add(activity);
                }
            };
            ActivitySource.AddActivityListener(_activityListener);
        }

        /// <summary>
        /// Cleanup activity listener after all tests.
        /// </summary>
        [ClassCleanup]
        public static void ClassCleanup()
        {
            _activityListener?.Dispose();
        }

        /// <summary>
        /// Clear recorded activities before each test.
        /// </summary>
        [TestInitialize]
        public void TestInitialize()
        {
            _recordedActivities.Clear();
        }

        #region Helpers

        /// <summary>
        /// Creates and starts a new MCP tool execution activity, asserting it was created.
        /// </summary>
        private static Activity CreateActivity()
        {
            Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");
            Assert.IsNotNull(activity, "Activity should be created");
            return activity;
        }

        /// <summary>
        /// Stops the activity and returns the first recorded activity, asserting it was captured.
        /// </summary>
        private static Activity StopAndGetRecordedActivity(Activity activity)
        {
            activity.Stop();
            Activity? recorded = _recordedActivities.FirstOrDefault();
            Assert.IsNotNull(recorded, "Activity should be recorded");
            return recorded;
        }

        /// <summary>
        /// Builds a minimal service provider for tests that don't need real services.
        /// </summary>
        private static IServiceProvider CreateServiceProvider()
        {
            return new ServiceCollection().BuildServiceProvider();
        }

        /// <summary>
        /// Creates a CallToolResult with the given text and error state.
        /// </summary>
        private static CallToolResult CreateToolResult(string text = "result", bool isError = false)
        {
            return new CallToolResult
            {
                Content = new List<ContentBlock> { new TextContentBlock { Text = text } },
                IsError = isError
            };
        }

        /// <summary>
        /// Creates an exception instance from a type name string, for use with DataRow tests.
        /// </summary>
        private static Exception CreateExceptionByTypeName(string typeName)
        {
            return typeName switch
            {
                nameof(OperationCanceledException) => new OperationCanceledException(),
                nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(),
                nameof(ArgumentException) => new ArgumentException(),
                nameof(InvalidOperationException) => new InvalidOperationException(),
                _ => new Exception()
            };
        }

        #endregion

        #region TrackMcpToolExecutionStarted

        /// <summary>
        /// Test that TrackMcpToolExecutionStarted sets the expected tags for various input combinations,
        /// including when optional parameters are null.
        /// </summary>
        [DataTestMethod]
        [DataRow("read_records", "books", "read", null, DisplayName = "Sets entity, operation; no procedure")]
        [DataRow("custom_proc", "CustomEntity", "execute", "dbo.CustomProc", DisplayName = "Custom tool with all tags including db.procedure")]
        [DataRow("describe_entities", null, "describe", null, DisplayName = "Describe tool with null entity")]
        [DataRow("custom_tool", "MyEntity", "execute", "schema.MyStoredProc", DisplayName = "Sets all four tags")]
        public void TrackMcpToolExecutionStarted_SetsExpectedTags(
            string toolName, string? entityName, string? operation, string? dbProcedure)
        {
            // Arrange & Act
            using Activity activity = CreateActivity();
            activity.TrackMcpToolExecutionStarted(
                toolName: toolName,
                entityName: entityName,
                operation: operation,
                dbProcedure: dbProcedure);

            Activity recorded = StopAndGetRecordedActivity(activity);

            // Assert — tool name is always set
            Assert.AreEqual(toolName, recorded.GetTagItem("mcp.tool.name"));

            // Optional tags: present only when supplied
            Assert.AreEqual(entityName, recorded.GetTagItem("dab.entity"));
            Assert.AreEqual(operation, recorded.GetTagItem("dab.operation"));
            Assert.AreEqual(dbProcedure, recorded.GetTagItem("db.procedure"));
        }

        #endregion

        #region TrackMcpToolExecutionFinished

        /// <summary>
        /// Test that TrackMcpToolExecutionFinished sets status to OK.
        /// </summary>
        [TestMethod]
        public void TrackMcpToolExecutionFinished_SetsStatusToOk()
        {
            using Activity activity = CreateActivity();
            activity.TrackMcpToolExecutionStarted(toolName: "read_records");
            activity.TrackMcpToolExecutionFinished();

            Activity recorded = StopAndGetRecordedActivity(activity);
            Assert.AreEqual(ActivityStatusCode.Ok, recorded.Status);
        }

        /// <summary>
        /// Test that TrackMcpToolExecutionFinishedWithException records exception and sets error status.
        /// </summary>
        [TestMethod]
        public void TrackMcpToolExecutionFinishedWithException_RecordsExceptionAndSetsErrorStatus()
        {
            using Activity activity = CreateActivity();
            activity.TrackMcpToolExecutionStarted(toolName: "read_records");

            Exception testException = new InvalidOperationException("Test exception");
            activity.TrackMcpToolExecutionFinishedWithException(testException, errorCode: McpTelemetryErrorCodes.EXECUTION_FAILED);

            Activity recorded = StopAndGetRecordedActivity(activity);
            Assert.AreEqual(ActivityStatusCode.Error, recorded.Status);
            Assert.AreEqual("Test exception", recorded.StatusDescription);
            Assert.AreEqual("InvalidOperationException", recorded.GetTagItem("error.type"));
            Assert.AreEqual("Test exception", recorded.GetTagItem("error.message"));
            Assert.AreEqual(McpTelemetryErrorCodes.EXECUTION_FAILED, recorded.GetTagItem("error.code"));

            ActivityEvent? exceptionEvent = recorded.Events.FirstOrDefault(e => e.Name == "exception");
            Assert.IsNotNull(exceptionEvent, "Exception event should be recorded");
        }

        #endregion

        #region InferOperationFromTool

        /// <summary>
        /// Test that InferOperationFromTool returns the correct operation for built-in and custom tools.
        /// Built-in tools are mapped by name; custom tools always return "execute".
        /// </summary>
        [DataTestMethod]
        // Built-in DML tool names mapped to operations
        [DataRow(ToolType.BuiltIn, "read_records", "read", DisplayName = "Built-in: read_records -> read")]
        [DataRow(ToolType.BuiltIn, "create_record", "create", DisplayName = "Built-in: create_record -> create")]
        [DataRow(ToolType.BuiltIn, "update_record", "update", DisplayName = "Built-in: update_record -> update")]
        [DataRow(ToolType.BuiltIn, "delete_record", "delete", DisplayName = "Built-in: delete_record -> delete")]
        [DataRow(ToolType.BuiltIn, "describe_entities", "describe", DisplayName = "Built-in: describe_entities -> describe")]
        [DataRow(ToolType.BuiltIn, "execute_entity", "execute", DisplayName = "Built-in: execute_entity -> execute")]
        [DataRow(ToolType.BuiltIn, "unknown_builtin", "execute", DisplayName = "Built-in: unknown -> execute (fallback)")]
        // Custom tools always return "execute"
        [DataRow(ToolType.Custom, "get_book", "execute", DisplayName = "Custom: get_book -> execute (stored proc)")]
        [DataRow(ToolType.Custom, "read_users", "execute", DisplayName = "Custom: read_users -> execute (ignore name)")]
        [DataRow(ToolType.Custom, "custom_proc", "execute", DisplayName = "Custom: custom_proc -> execute")]
        public void InferOperationFromTool_ReturnsCorrectOperation(ToolType toolType, string toolName, string expectedOperation)
        {
            IMcpTool tool = new MockMcpTool(CreateToolResult(), toolType);
            Assert.AreEqual(expectedOperation, McpTelemetryHelper.InferOperationFromTool(tool, toolName));
        }

        #endregion

        #region MapExceptionToErrorCode

        /// <summary>
        /// Test that MapExceptionToErrorCode returns the correct error code for each exception type.
        /// </summary>
        [DataTestMethod]
        [DataRow("OperationCanceledException", McpTelemetryErrorCodes.OPERATION_CANCELLED)]
        [DataRow("UnauthorizedAccessException", McpTelemetryErrorCodes.AUTHORIZATION_FAILED)]
        [DataRow("ArgumentException", McpTelemetryErrorCodes.INVALID_REQUEST)]
        [DataRow("InvalidOperationException", McpTelemetryErrorCodes.EXECUTION_FAILED)]
        [DataRow("Exception", McpTelemetryErrorCodes.EXECUTION_FAILED)]
        public void MapExceptionToErrorCode_ReturnsCorrectCode(string exceptionTypeName, string expectedErrorCode)
        {
            Exception ex = CreateExceptionByTypeName(exceptionTypeName);
            Assert.AreEqual(expectedErrorCode, McpTelemetryHelper.MapExceptionToErrorCode(ex));
        }

        #endregion

        #region ExecuteWithTelemetryAsync

        /// <summary>
        /// Test that ExecuteWithTelemetryAsync sets Ok status and correct operation for all built-in DML tools.
        /// </summary>
        [DataTestMethod]
        [DataRow("read_records", "read", DisplayName = "read_records -> read operation")]
        [DataRow("create_record", "create", DisplayName = "create_record -> create operation")]
        [DataRow("update_record", "update", DisplayName = "update_record -> update operation")]
        [DataRow("delete_record", "delete", DisplayName = "delete_record -> delete operation")]
        [DataRow("describe_entities", "describe", DisplayName = "describe_entities -> describe operation")]
        [DataRow("execute_entity", "execute", DisplayName = "execute_entity -> execute operation")]
        public async Task ExecuteWithTelemetryAsync_SetsOkStatusAndCorrectOperation_ForBuiltInTools(
            string toolName, string expectedOperation)
        {
            CallToolResult expectedResult = CreateToolResult("success");
            IMcpTool tool = new MockMcpTool(expectedResult, ToolType.BuiltIn);

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, toolName, arguments: null, CreateServiceProvider(), CancellationToken.None);

            Assert.AreSame(expectedResult, result);
            Activity recorded = _recordedActivities.First();
            Assert.AreEqual(ActivityStatusCode.Ok, recorded.Status);
            Assert.AreEqual(toolName, recorded.GetTagItem("mcp.tool.name"));
            Assert.AreEqual(expectedOperation, recorded.GetTagItem("dab.operation"));
        }

        /// <summary>
        /// Test that ExecuteWithTelemetryAsync always sets operation to "execute" for custom tools (stored procedures).
        /// </summary>
        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_SetsExecuteOperation_ForCustomTools()
        {
            CallToolResult expectedResult = CreateToolResult("success");
            IMcpTool tool = new MockMcpTool(expectedResult, ToolType.Custom);

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "get_book", arguments: null, CreateServiceProvider(), CancellationToken.None);

            Assert.AreSame(expectedResult, result);
            Activity recorded = _recordedActivities.First();
            Assert.AreEqual(ActivityStatusCode.Ok, recorded.Status);
            Assert.AreEqual("get_book", recorded.GetTagItem("mcp.tool.name"));
            Assert.AreEqual("execute", recorded.GetTagItem("dab.operation"));
        }

        /// <summary>
        /// Test that ExecuteWithTelemetryAsync sets Error status when tool returns IsError=true.
        /// </summary>
        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_SetsErrorStatus_WhenToolReturnsIsError()
        {
            CallToolResult errorResult = CreateToolResult("error occurred", isError: true);
            IMcpTool tool = new MockMcpTool(errorResult, ToolType.BuiltIn);

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "create_record", arguments: null, CreateServiceProvider(), CancellationToken.None);

            Assert.AreSame(errorResult, result);
            Activity recorded = _recordedActivities.First();
            Assert.AreEqual(ActivityStatusCode.Error, recorded.Status);
            Assert.AreEqual(true, recorded.GetTagItem("mcp.tool.error"));
        }

        /// <summary>
        /// Test that ExecuteWithTelemetryAsync records exception and re-throws when tool throws.
        /// </summary>
        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_RecordsExceptionAndRethrows_WhenToolThrows()
        {
            InvalidOperationException expectedException = new("tool exploded");
            IMcpTool tool = new MockMcpTool(expectedException, ToolType.BuiltIn);

            InvalidOperationException thrownEx = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => McpTelemetryHelper.ExecuteWithTelemetryAsync(
                    tool, "delete_record", arguments: null, CreateServiceProvider(), CancellationToken.None));

            Assert.AreEqual("tool exploded", thrownEx.Message);

            Activity recorded = _recordedActivities.First();
            Assert.AreEqual(ActivityStatusCode.Error, recorded.Status);
            Assert.AreEqual("InvalidOperationException", recorded.GetTagItem("error.type"));
            Assert.AreEqual(McpTelemetryErrorCodes.EXECUTION_FAILED, recorded.GetTagItem("error.code"));

            ActivityEvent? exceptionEvent = recorded.Events.FirstOrDefault(e => e.Name == "exception");
            Assert.IsNotNull(exceptionEvent, "Exception event should be recorded");
        }

        #endregion

        #region Test Mocks

        /// <summary>
        /// A minimal mock IMcpTool for testing ExecuteWithTelemetryAsync.
        /// Returns a predetermined result or throws a predetermined exception.
        /// </summary>
        private class MockMcpTool : IMcpTool
        {
            private readonly CallToolResult? _result;
            private readonly Exception? _exception;

            public MockMcpTool(CallToolResult result, ToolType toolType = ToolType.BuiltIn)
            {
                _result = result;
                ToolType = toolType;
            }

            public MockMcpTool(Exception exception, ToolType toolType = ToolType.BuiltIn)
            {
                _exception = exception;
                ToolType = toolType;
            }

            public ToolType ToolType { get; }

            public Tool GetToolMetadata() => new() { Name = "mock_tool", Description = "Mock tool for testing" };

            public Task<CallToolResult> ExecuteAsync(JsonDocument? arguments, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            {
                if (_exception != null)
                {
                    throw _exception;
                }

                return Task.FromResult(_result!);
            }
        }

        #endregion
    }
}

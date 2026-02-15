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
        private static List<Activity> _recordedActivities = new();

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
                Content = new List<ContentBlock> { new TextContentBlock { Type = "text", Text = text } },
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
        [DataRow("get_book", "GetBook", "execute", "dbo.GetBookById", DisplayName = "Sets all tags including db.procedure")]
        [DataRow("describe_entities", null, null, null, DisplayName = "Handles all-null optional params")]
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
            activity.TrackMcpToolExecutionFinishedWithException(testException, errorCode: "ExecutionFailed");

            Activity recorded = StopAndGetRecordedActivity(activity);
            Assert.AreEqual(ActivityStatusCode.Error, recorded.Status);
            Assert.AreEqual("Test exception", recorded.StatusDescription);
            Assert.AreEqual("InvalidOperationException", recorded.GetTagItem("error.type"));
            Assert.AreEqual("Test exception", recorded.GetTagItem("error.message"));
            Assert.AreEqual("ExecutionFailed", recorded.GetTagItem("error.code"));

            ActivityEvent? exceptionEvent = recorded.Events.FirstOrDefault(e => e.Name == "exception");
            Assert.IsNotNull(exceptionEvent, "Exception event should be recorded");
        }

        #endregion

        #region InferOperationFromToolName

        /// <summary>
        /// Test that InferOperationFromToolName returns the correct operation for tool names,
        /// including built-in names, keyword variants, ambiguous names (precedence), and unknown names.
        /// </summary>
        [DataTestMethod]
        // Built-in tool names
        [DataRow("read_records", "read")]
        [DataRow("get_book", "read")]
        [DataRow("list_items", "read")]
        [DataRow("describe_entities", "read")]
        [DataRow("create_record", "create")]
        [DataRow("insert_book", "create")]
        [DataRow("update_record", "update")]
        [DataRow("modify_entry", "update")]
        [DataRow("delete_record", "delete")]
        [DataRow("remove_item", "delete")]
        [DataRow("execute_entity", "execute")]
        // Unknown / default
        [DataRow("my_custom_proc", "execute")]
        [DataRow("unknown_tool", "execute")]
        // Precedence: first matching keyword wins
        [DataRow("get_deleted_items", "read", DisplayName = "'get' matches before 'delete'")]
        [DataRow("list_updates", "read", DisplayName = "'list' matches before 'update'")]
        [DataRow("create_log", "create", DisplayName = "'create' matches without 'read' substring")]
        public void InferOperationFromToolName_ReturnsCorrectOperation(string toolName, string expectedOperation)
        {
            Assert.AreEqual(expectedOperation, McpTelemetryHelper.InferOperationFromToolName(toolName));
        }

        #endregion

        #region MapExceptionToErrorCode

        /// <summary>
        /// Test that MapExceptionToErrorCode returns the correct error code for each exception type.
        /// </summary>
        [DataTestMethod]
        [DataRow("OperationCanceledException", "OperationCancelled")]
        [DataRow("UnauthorizedAccessException", "AuthenticationFailed")]
        [DataRow("ArgumentException", "InvalidRequest")]
        [DataRow("InvalidOperationException", "ExecutionFailed")]
        [DataRow("Exception", "ExecutionFailed")]
        public void MapExceptionToErrorCode_ReturnsCorrectCode(string exceptionTypeName, string expectedErrorCode)
        {
            Exception ex = CreateExceptionByTypeName(exceptionTypeName);
            Assert.AreEqual(expectedErrorCode, McpTelemetryHelper.MapExceptionToErrorCode(ex));
        }

        #endregion

        #region ExecuteWithTelemetryAsync

        /// <summary>
        /// Test that ExecuteWithTelemetryAsync sets Ok status for a successful tool execution.
        /// </summary>
        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_SetsOkStatus_OnSuccess()
        {
            CallToolResult expectedResult = CreateToolResult("success");
            IMcpTool tool = new FakeMcpTool(expectedResult);

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "read_records", arguments: null, CreateServiceProvider(), CancellationToken.None);

            Assert.AreSame(expectedResult, result);
            Activity recorded = _recordedActivities.First();
            Assert.AreEqual(ActivityStatusCode.Ok, recorded.Status);
            Assert.AreEqual("read_records", recorded.GetTagItem("mcp.tool.name"));
            Assert.AreEqual("read", recorded.GetTagItem("dab.operation"));
        }

        /// <summary>
        /// Test that ExecuteWithTelemetryAsync sets Error status when tool returns IsError=true.
        /// </summary>
        [TestMethod]
        public async Task ExecuteWithTelemetryAsync_SetsErrorStatus_WhenToolReturnsIsError()
        {
            CallToolResult errorResult = CreateToolResult("error occurred", isError: true);
            IMcpTool tool = new FakeMcpTool(errorResult);

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
            IMcpTool tool = new FakeMcpTool(expectedException);

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

        #region Test Fakes

        /// <summary>
        /// A minimal fake IMcpTool for testing ExecuteWithTelemetryAsync.
        /// Returns a predetermined result or throws a predetermined exception.
        /// </summary>
        private class FakeMcpTool : IMcpTool
        {
            private readonly CallToolResult? _result;
            private readonly Exception? _exception;

            public FakeMcpTool(CallToolResult result) => _result = result;
            public FakeMcpTool(Exception exception) => _exception = exception;

            public ToolType ToolType => ToolType.BuiltIn;

            public Tool GetToolMetadata() => new() { Name = "fake_tool", Description = "Fake tool for testing" };

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

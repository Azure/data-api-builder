// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Azure.DataApiBuilder.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
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

        /// <summary>
        /// Test that TrackMcpToolExecutionStarted sets the correct tags.
        /// </summary>
        [TestMethod]
        public void TrackMcpToolExecutionStarted_SetsCorrectTags()
        {
            // Arrange
            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");
            Assert.IsNotNull(activity, "Activity should be created");

            // Act
            activity.TrackMcpToolExecutionStarted(
                toolName: "read_records",
                entityName: "books",
                operation: "read",
                dbProcedure: null);

            activity.Stop();

            // Assert
            Activity? recordedActivity = _recordedActivities.FirstOrDefault();
            Assert.IsNotNull(recordedActivity, "Activity should be recorded");
            Assert.AreEqual("read_records", recordedActivity.GetTagItem("mcp.tool.name"));
            Assert.AreEqual("books", recordedActivity.GetTagItem("dab.entity"));
            Assert.AreEqual("read", recordedActivity.GetTagItem("dab.operation"));
        }

        /// <summary>
        /// Test that TrackMcpToolExecutionStarted sets db.procedure tag when provided.
        /// </summary>
        [TestMethod]
        public void TrackMcpToolExecutionStarted_SetsDbProcedureTag_WhenProvided()
        {
            // Arrange
            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");
            Assert.IsNotNull(activity, "Activity should be created");

            // Act
            activity.TrackMcpToolExecutionStarted(
                toolName: "get_book",
                entityName: "GetBook",
                operation: "execute",
                dbProcedure: "dbo.GetBookById");

            activity.Stop();

            // Assert
            Activity? recordedActivity = _recordedActivities.FirstOrDefault();
            Assert.IsNotNull(recordedActivity, "Activity should be recorded");
            Assert.AreEqual("get_book", recordedActivity.GetTagItem("mcp.tool.name"));
            Assert.AreEqual("GetBook", recordedActivity.GetTagItem("dab.entity"));
            Assert.AreEqual("execute", recordedActivity.GetTagItem("dab.operation"));
            Assert.AreEqual("dbo.GetBookById", recordedActivity.GetTagItem("db.procedure"));
        }

        /// <summary>
        /// Test that TrackMcpToolExecutionFinished sets status to OK.
        /// </summary>
        [TestMethod]
        public void TrackMcpToolExecutionFinished_SetsStatusToOk()
        {
            // Arrange
            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");
            Assert.IsNotNull(activity, "Activity should be created");

            activity.TrackMcpToolExecutionStarted(toolName: "read_records");

            // Act
            activity.TrackMcpToolExecutionFinished();
            activity.Stop();

            // Assert
            Activity? recordedActivity = _recordedActivities.FirstOrDefault();
            Assert.IsNotNull(recordedActivity, "Activity should be recorded");
            Assert.AreEqual(ActivityStatusCode.Ok, recordedActivity.Status);
        }

        /// <summary>
        /// Test that TrackMcpToolExecutionFinishedWithException records exception and sets error status.
        /// </summary>
        [TestMethod]
        public void TrackMcpToolExecutionFinishedWithException_RecordsExceptionAndSetsErrorStatus()
        {
            // Arrange
            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");
            Assert.IsNotNull(activity, "Activity should be created");

            activity.TrackMcpToolExecutionStarted(toolName: "read_records");

            Exception testException = new InvalidOperationException("Test exception");

            // Act
            activity.TrackMcpToolExecutionFinishedWithException(testException, errorCode: "ExecutionFailed");
            activity.Stop();

            // Assert
            Activity? recordedActivity = _recordedActivities.FirstOrDefault();
            Assert.IsNotNull(recordedActivity, "Activity should be recorded");
            Assert.AreEqual(ActivityStatusCode.Error, recordedActivity.Status);
            Assert.AreEqual("Test exception", recordedActivity.StatusDescription);
            Assert.AreEqual("InvalidOperationException", recordedActivity.GetTagItem("error.type"));
            Assert.AreEqual("Test exception", recordedActivity.GetTagItem("error.message"));
            Assert.AreEqual("ExecutionFailed", recordedActivity.GetTagItem("error.code"));

            // Check that exception was recorded
            ActivityEvent? exceptionEvent = recordedActivity.Events.FirstOrDefault(e => e.Name == "exception");
            Assert.IsNotNull(exceptionEvent, "Exception event should be recorded");
        }

        /// <summary>
        /// Test that TrackMcpToolExecutionStarted handles null optional parameters gracefully.
        /// </summary>
        [TestMethod]
        public void TrackMcpToolExecutionStarted_HandlesNullOptionalParameters()
        {
            // Arrange
            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");
            Assert.IsNotNull(activity, "Activity should be created");

            // Act - only provide tool name, all others are null
            activity.TrackMcpToolExecutionStarted(toolName: "describe_entities");
            activity.Stop();

            // Assert
            Activity? recordedActivity = _recordedActivities.FirstOrDefault();
            Assert.IsNotNull(recordedActivity, "Activity should be recorded");
            Assert.AreEqual("describe_entities", recordedActivity.GetTagItem("mcp.tool.name"));
            Assert.IsNull(recordedActivity.GetTagItem("dab.entity"));
            Assert.IsNull(recordedActivity.GetTagItem("dab.operation"));
            Assert.IsNull(recordedActivity.GetTagItem("db.procedure"));
        }

        /// <summary>
        /// Test that multiple tags can be set on the same activity.
        /// </summary>
        [TestMethod]
        public void TrackMcpToolExecutionStarted_SupportsMultipleTags()
        {
            // Arrange
            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");
            Assert.IsNotNull(activity, "Activity should be created");

            // Act
            activity.TrackMcpToolExecutionStarted(
                toolName: "custom_tool",
                entityName: "MyEntity",
                operation: "execute",
                dbProcedure: "schema.MyStoredProc");

            activity.Stop();

            // Assert
            Activity? recordedActivity = _recordedActivities.FirstOrDefault();
            Assert.IsNotNull(recordedActivity, "Activity should be recorded");

            // Verify all tags are present
            Assert.AreEqual(4, recordedActivity.Tags.Count(t =>
                t.Key == "mcp.tool.name" ||
                t.Key == "dab.entity" ||
                t.Key == "dab.operation" ||
                t.Key == "db.procedure"));

            Assert.AreEqual("custom_tool", recordedActivity.GetTagItem("mcp.tool.name"));
            Assert.AreEqual("MyEntity", recordedActivity.GetTagItem("dab.entity"));
            Assert.AreEqual("execute", recordedActivity.GetTagItem("dab.operation"));
            Assert.AreEqual("schema.MyStoredProc", recordedActivity.GetTagItem("db.procedure"));
        }
    }
}

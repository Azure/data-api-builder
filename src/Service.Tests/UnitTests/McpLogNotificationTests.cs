// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the MCP logging notification components.
    /// </summary>
    [TestClass]
    public class McpLogNotificationTests
    {
        [TestMethod]
        public void McpLogNotificationWriter_IsEnabledFalseByDefault()
        {
            // Arrange & Act
            McpLogNotificationWriter writer = new();

            // Assert
            Assert.IsFalse(writer.IsEnabled);
        }

        [TestMethod]
        public void McpLogNotificationWriter_CanBeEnabled()
        {
            // Arrange
            McpLogNotificationWriter writer = new()
            {
                // Act
                IsEnabled = true
            };

            // Assert
            Assert.IsTrue(writer.IsEnabled);
        }

        [TestMethod]
        public void McpLogger_IsEnabledReturnsFalse_WhenWriterDisabled()
        {
            // Arrange
            McpLogNotificationWriter writer = new()
            {
                IsEnabled = false
            };
            McpLogger logger = new("TestCategory", writer);

            // Act & Assert
            Assert.IsFalse(logger.IsEnabled(LogLevel.Information));
            Assert.IsFalse(logger.IsEnabled(LogLevel.Error));
        }

        [TestMethod]
        public void McpLogger_IsEnabledReturnsTrue_WhenWriterEnabled()
        {
            // Arrange
            McpLogNotificationWriter writer = new()
            {
                IsEnabled = true
            };
            McpLogger logger = new("TestCategory", writer);

            // Act & Assert
            Assert.IsTrue(logger.IsEnabled(LogLevel.Information));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Error));
        }

        [TestMethod]
        public void McpLogger_NoneLevel_AlwaysReturnsFalse()
        {
            // Arrange
            McpLogNotificationWriter writer = new()
            {
                IsEnabled = true
            };
            McpLogger logger = new("TestCategory", writer);

            // Act & Assert - LogLevel.None should always be disabled
            Assert.IsFalse(logger.IsEnabled(LogLevel.None));
        }

        [TestMethod]
        public void McpLoggerProvider_CreatesSameLoggerForSameCategory()
        {
            // Arrange
            McpLogNotificationWriter writer = new();
            McpLoggerProvider provider = new(writer);

            // Act
            ILogger logger1 = provider.CreateLogger("TestCategory");
            ILogger logger2 = provider.CreateLogger("TestCategory");
            ILogger logger3 = provider.CreateLogger("OtherCategory");

            // Assert - same category should return same logger instance
            Assert.AreSame(logger1, logger2);
            Assert.AreNotSame(logger1, logger3);
        }

        /// <summary>
        /// When constructed without a backing <see cref="McpStdoutWriter"/>
        /// (the unit-test default), <see cref="McpLogNotificationWriter.WriteNotification"/>
        /// must be a silent no-op rather than throwing a NullReferenceException.
        /// This guards the safety net for tests and any non-stdio host that
        /// constructs the type without a stdout sink.
        /// </summary>
        [TestMethod]
        public void WriteNotification_DoesNotThrow_WhenStdoutWriterIsNull()
        {
            // Arrange — null stdout writer is the default ctor path.
            McpLogNotificationWriter writer = new()
            {
                IsEnabled = true
            };

            // Act & Assert — must not throw.
            writer.WriteNotification(LogLevel.Information, "TestCategory", "hello");
        }

        /// <summary>
        /// End-to-end of the notification pipeline: when wired to a real
        /// <see cref="McpStdoutWriter"/>, <see cref="McpLogNotificationWriter.WriteNotification"/>
        /// must emit a single, well-formed MCP <c>notifications/message</c>
        /// frame (jsonrpc + method + params { level, logger, data }).
        /// Verifies framing contract + exact JSON structure.
        /// </summary>
        [TestMethod]
        public void WriteNotification_EmitsValidMcpFrame()
        {
            // Arrange — back the stdout writer with an in-memory stream so we
            // can inspect the exact bytes emitted.
            using MemoryStream ms = new();
            StreamWriter inner = new(
                ms,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: -1,
                leaveOpen: true)
            {
                AutoFlush = true
            };
            using McpStdoutWriter stdout = new(inner);
            McpLogNotificationWriter writer = new(stdout)
            {
                IsEnabled = true
            };

            // Act
            writer.WriteNotification(LogLevel.Warning, "MyApp.SomeService", "uh oh");

            // Assert — single line, valid JSON, correct shape.
            ms.Position = 0;
            string content = new StreamReader(ms).ReadToEnd().TrimEnd();
            Assert.IsFalse(string.IsNullOrEmpty(content), "No frame written.");

            using JsonDocument doc = JsonDocument.Parse(content);
            JsonElement root = doc.RootElement;

            Assert.AreEqual("2.0", root.GetProperty("jsonrpc").GetString());
            Assert.AreEqual("notifications/message", root.GetProperty("method").GetString());

            JsonElement paramsElem = root.GetProperty("params");
            Assert.AreEqual("warning", paramsElem.GetProperty("level").GetString(),
                "MCP level should be lowercase per spec.");
            Assert.AreEqual("MyApp.SomeService", paramsElem.GetProperty("logger").GetString());
            Assert.AreEqual("uh oh", paramsElem.GetProperty("data").GetString());
        }

        /// <summary>
        /// Single-source-of-truth gate: when the writer's <c>IsEnabled</c> is
        /// false, <see cref="McpLogger.IsEnabled(LogLevel)"/> must return false
        /// for any non-None level so the logging framework never invokes the
        /// formatter. This protects callers from doing unnecessary string work.
        /// </summary>
        [TestMethod]
        public void McpLogger_GateBlocksAllLevels_WhenWriterDisabled()
        {
            // Arrange
            McpLogNotificationWriter writer = new()
            {
                IsEnabled = false
            };
            McpLogger logger = new("Cat", writer);

            // Act & Assert — every non-None level is blocked when writer is off.
            foreach (LogLevel level in new[]
                     {
                         LogLevel.Trace, LogLevel.Debug, LogLevel.Information,
                         LogLevel.Warning, LogLevel.Error, LogLevel.Critical
                     })
            {
                Assert.IsFalse(logger.IsEnabled(level),
                    $"Level {level} should be disabled when writer.IsEnabled=false.");
            }
        }

        /// <summary>
        /// Flipping <see cref="McpLogNotificationWriter.IsEnabled"/> at runtime
        /// (which is what MCP <c>logging/setLevel</c> does indirectly) must
        /// take immediate effect on subsequent <see cref="McpLogger.IsEnabled(LogLevel)"/>
        /// calls. Confirms the property is the live single source of truth and
        /// not cached anywhere downstream.
        /// </summary>
        [TestMethod]
        public void McpLogger_RespectsRuntimeIsEnabledFlip()
        {
            // Arrange — start disabled.
            McpLogNotificationWriter writer = new()
            {
                IsEnabled = false
            };
            McpLogger logger = new("Cat", writer);
            Assert.IsFalse(logger.IsEnabled(LogLevel.Information));

            // Act — flip the gate on.
            writer.IsEnabled = true;

            // Assert — logger reflects the new state immediately.
            Assert.IsTrue(logger.IsEnabled(LogLevel.Information));

            // Flip back off — must propagate again.
            writer.IsEnabled = false;
            Assert.IsFalse(logger.IsEnabled(LogLevel.Information));
        }
    }
}

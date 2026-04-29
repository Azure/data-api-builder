// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

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
            McpLogger logger = new("TestCategory", writer, _ => true);

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
            McpLogger logger = new("TestCategory", writer, _ => true);

            // Act & Assert
            Assert.IsTrue(logger.IsEnabled(LogLevel.Information));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Error));
        }

        [TestMethod]
        public void McpLogger_RespectsLevelFilter()
        {
            // Arrange
            McpLogNotificationWriter writer = new()
            {
                IsEnabled = true
            };

            // Filter that only allows Warning and above
            McpLogger logger = new("TestCategory", writer, level => level >= LogLevel.Warning);

            // Act & Assert
            Assert.IsFalse(logger.IsEnabled(LogLevel.Debug));
            Assert.IsFalse(logger.IsEnabled(LogLevel.Information));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Warning));
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
            McpLogger logger = new("TestCategory", writer, _ => true);

            // Act & Assert - LogLevel.None should always be disabled
            Assert.IsFalse(logger.IsEnabled(LogLevel.None));
        }

        [TestMethod]
        public void McpLoggerProvider_CreatesSameLoggerForSameCategory()
        {
            // Arrange
            McpLogNotificationWriter writer = new();
            McpLoggerProvider provider = new(writer, _ => true);

            // Act
            ILogger logger1 = provider.CreateLogger("TestCategory");
            ILogger logger2 = provider.CreateLogger("TestCategory");
            ILogger logger3 = provider.CreateLogger("OtherCategory");

            // Assert - same category should return same logger instance
            Assert.AreSame(logger1, logger2);
            Assert.AreNotSame(logger1, logger3);
        }
    }
}

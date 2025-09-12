// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Tests for Export Command in CLI.
/// </summary>
[TestClass]
public class ExporterTests
{
    /// <summary>
    /// Tests the ExportGraphQLFromDabService method to ensure it logs correctly when the HTTPS endpoint works.
    /// </summary>
    [TestMethod]
    public void ExportGraphQLFromDabService_LogsWhenHttpsWorks()
    {
        // Arrange
        Mock<ILogger> mockLogger = new();
        Mock<Exporter> mockExporter = new();
        RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "", new()),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>())
            );

        // Setup the mock to return a schema when the HTTPS endpoint is used
        mockExporter.Setup(e => e.GetGraphQLSchema(runtimeConfig, false))
                    .Returns("schema from HTTPS endpoint");

        // Act
        string result = mockExporter.Object.ExportGraphQLFromDabService(runtimeConfig, mockLogger.Object);

        // Assert
        Assert.AreEqual("schema from HTTPS endpoint", result);
        mockLogger.Verify(logger => logger.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("schema from HTTPS endpoint.")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()!), Times.Never);
    }

    /// <summary>
    /// Tests the ExportGraphQLFromDabService method to ensure it logs correctly when the HTTPS endpoint fails and falls back to the HTTP endpoint.
    /// This test verifies that:
    /// 1. The method attempts to fetch the schema using the HTTPS endpoint first.
    /// 2. If the HTTPS endpoint fails, it logs the failure and attempts to fetch the schema using the HTTP endpoint.
    /// 3. The method logs the appropriate messages during the process.
    /// 4. The method returns the schema fetched from the HTTP endpoint when the HTTPS endpoint fails.
    /// </summary>
    [TestMethod]
    public void ExportGraphQLFromDabService_LogsFallbackToHttp_WhenHttpsFails()
    {
        // Arrange
        Mock<ILogger> mockLogger = new();
        Mock<Exporter> mockExporter = new();
        RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "", new()),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>())
            );

        mockExporter.Setup(e => e.GetGraphQLSchema(runtimeConfig, false))
                    .Throws(new Exception("HTTPS endpoint failed"));
        mockExporter.Setup(e => e.GetGraphQLSchema(runtimeConfig, true))
                    .Returns("Fallback schema");

        // Act
        string result = mockExporter.Object.ExportGraphQLFromDabService(runtimeConfig, mockLogger.Object);

        // Assert
        Assert.AreEqual("Fallback schema", result);
        mockLogger.Verify(logger => logger.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Trying to fetch schema from DAB Service using HTTPS endpoint.")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()!), Times.Once);

        mockLogger.Verify(logger => logger.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to fetch schema from DAB Service using HTTPS endpoint. Trying with HTTP endpoint.")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()!), Times.Once);
    }

    /// <summary>
    /// Tests the ExportGraphQLFromDabService method to ensure it throws an exception when both the HTTPS and HTTP endpoints fail.
    /// This test verifies that:
    /// 1. The method attempts to fetch the schema using the HTTPS endpoint first.
    /// 2. If the HTTPS endpoint fails, it logs the failure and attempts to fetch the schema using the HTTP endpoint.
    /// 3. If both endpoints fail, the method throws an exception.
    /// 4. The method logs the appropriate messages during the process.
    /// </summary>
    [TestMethod]
    public void ExportGraphQLFromDabService_ThrowsException_WhenBothHttpsAndHttpFail()
    {
        // Arrange
        Mock<ILogger> mockLogger = new();
        Mock<Exporter> mockExporter = new() { CallBase = true };
        RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "", new()),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>())
            );

        // Setup the mock to throw an exception when the HTTPS endpoint is used
        mockExporter.Setup(e => e.GetGraphQLSchema(runtimeConfig, false))
                    .Throws(new Exception("HTTPS endpoint failed"));

        // Setup the mock to throw an exception when the HTTP endpoint is used
        mockExporter.Setup(e => e.GetGraphQLSchema(runtimeConfig, true))
                    .Throws(new Exception("Both HTTP and HTTPS endpoint failed"));

        // Act & Assert
        // Verify that the method throws an exception when both endpoints fail
        Exception exception = Assert.ThrowsException<Exception>(() =>
            mockExporter.Object.ExportGraphQLFromDabService(runtimeConfig, mockLogger.Object));

        Assert.AreEqual("Both HTTP and HTTPS endpoint failed", exception.Message);

        // Verify that the correct log message is generated when attempting to use the HTTPS endpoint
        mockLogger.Verify(logger => logger.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Trying to fetch schema from DAB Service using HTTPS endpoint.")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()!), Times.Once);

        // Verify that the correct log message is generated when falling back to the HTTP endpoint
        mockLogger.Verify(logger => logger.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to fetch schema from DAB Service using HTTPS endpoint. Trying with HTTP endpoint.")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()!), Times.Once);
    }
}

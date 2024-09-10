// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// using System;
// using System.Net.Http;
// using System.Threading.Tasks;
// using Moq;
// using Xunit;
// using HotChocolate.Language;
// using System.IO.Abstractions;
// using System.IO.Abstractions.TestingHelpers;
// using HotChocolate.Utilities.Introspection;

namespace Cli.Tests;

[TestClass]
public class ExporterTests
{
    // [TestMethod]
    // public void ExportGraphQL_RetriesWithHttpIfHttpsFails()
    // {
    //     // Arrange
    //     Mock<HttpClient> httpsClientMock = new(MockBehavior.Strict);
    //     Mock<HttpClient> httpClientMock = new(MockBehavior.Strict);
    //     Mock<IntrospectionClient> introspectionClientMock = new();
    //     MockFileSystem fileSystem = new();

    //     ExportOptions options = new(true, "output", null, null);
    //     RuntimeConfig runtimeConfig = new(
    //             Schema: "schema",
    //             DataSource: new DataSource(DatabaseType.MSSQL, "", new()),
    //             Runtime: new(Rest: new(), GraphQL: new(), Host: new(null, null)),
    //             Entities: new(new Dictionary<string, Entity>())
    //         );

    //     // Setup the HTTPS client to throw an exception
    //     introspectionClientMock
    //         .Setup(client => client.DownloadSchemaAsync(It.IsAny<HttpClient>()))
    //         .ThrowsAsync(new Exception("HTTPS request failed"));

    //     // Setup the HTTP client to return a valid schema
    //     DocumentNode expectedSchema = new DocumentNode();
    //     introspectionClientMock
    //         .Setup(client => client.DownloadSchemaAsync(It.Is<HttpClient>(c => c.BaseAddress.Scheme == Uri.UriSchemeHttp)))
    //         .ReturnsAsync(expectedSchema);

    //     // Act
    //     Exporter.ExportGraphQL(options, runtimeConfig, fileSystem);

    //     // Assert
    //     introspectionClientMock.Verify(client => client.DownloadSchemaAsync(It.Is<HttpClient>(c => c.BaseAddress.Scheme == Uri.UriSchemeHttps)), Times.Once);
    //     introspectionClientMock.Verify(client => client.DownloadSchemaAsync(It.Is<HttpClient>(c => c.BaseAddress.Scheme == Uri.UriSchemeHttp)), Times.Once);
    //     Assert.IsTrue(fileSystem.FileExists("output/schema.graphql"));
    //     Assert.AreEqual(expectedSchema.ToString(), fileSystem.File.ReadAllText("output/schema.graphql"));
    // }

    [TestMethod]
    public void ExportGraphQLFromDabService_LogsFallbackToHttp_WhenHttpsFails()
    {
        // Arrange
        Mock<ILogger> mockLogger = new ();
        Mock<Exporter> mockExporter = new ();
        RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "", new()),
                Runtime: new(Rest: new(), GraphQL: new(), Host: new(null, null)),
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
}
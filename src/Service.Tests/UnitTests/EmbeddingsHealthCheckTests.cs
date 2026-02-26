// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.HealthCheck;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.HealthCheck;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for the embeddings health check logic in <see cref="HealthCheckHelper"/>.
/// The private method <c>UpdateEmbeddingsHealthCheckResultsAsync</c> is tested indirectly
/// through the public <see cref="HealthCheckHelper.GetHealthCheckResponseAsync"/> method.
/// Data source and entity health checks are disabled to isolate embeddings health check behavior.
/// </summary>
[TestClass]
public class EmbeddingsHealthCheckTests
{
    private Mock<ILogger<HealthCheckHelper>> _mockLogger = null!;
    private Mock<IEmbeddingService> _mockEmbeddingService = null!;
    private HttpUtilities _httpUtilities = null!;

    private const string TIME_EXCEEDED_ERROR_MESSAGE = "The threshold for executing the request has exceeded.";
    private const string DIMENSIONS_MISMATCH_ERROR_MESSAGE = "The embedding dimensions do not match the expected dimensions.";

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<HealthCheckHelper>>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();

        // Create HttpUtilities with mocked dependencies.
        // HttpUtilities won't be called since data source and entity health checks are disabled.
        Mock<ILogger<HttpUtilities>> httpLogger = new();
        Mock<IMetadataProviderFactory> metadataProviderFactory = new();
        Mock<RuntimeConfigLoader> mockLoader = new(null, null);
        Mock<RuntimeConfigProvider> mockConfigProvider = new(mockLoader.Object);
        Mock<IHttpClientFactory> mockHttpClientFactory = new();
        mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient { BaseAddress = new Uri("http://localhost:5000") });

        _httpUtilities = new HttpUtilities(
            httpLogger.Object,
            metadataProviderFactory.Object,
            mockConfigProvider.Object,
            mockHttpClientFactory.Object);
    }

    #region Healthy Scenarios

    /// <summary>
    /// Validates that when embedding succeeds within threshold and no dimension check is configured,
    /// the health check entry reports Healthy status.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_ReportsHealthy_WhenEmbeddingSucceedsWithinThreshold()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f, 0.3f };
        SetupSuccessfulEmbedding(embedding);

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: 60000));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.AreEqual(HealthStatus.Healthy, embeddingCheck.Status);
        Assert.AreEqual("embeddings", embeddingCheck.Name);
        Assert.IsNull(embeddingCheck.Exception);
        Assert.IsNotNull(embeddingCheck.ResponseTimeData);
        Assert.IsTrue(embeddingCheck.ResponseTimeData!.ResponseTimeMs >= 0);
        Assert.AreEqual(60000, embeddingCheck.ResponseTimeData.ThresholdMs);
        CollectionAssert.Contains(embeddingCheck.Tags!, HealthCheckConstants.EMBEDDING);
    }

    /// <summary>
    /// Validates that when embedding succeeds and dimensions match the expected value,
    /// the health check entry reports Healthy status.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_ReportsHealthy_WhenDimensionsMatch()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f, 0.3f };
        SetupSuccessfulEmbedding(embedding);

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(
                enabled: true,
                thresholdMs: 60000,
                expectedDimensions: 3));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.AreEqual(HealthStatus.Healthy, embeddingCheck.Status);
        Assert.IsNull(embeddingCheck.Exception);
    }

    /// <summary>
    /// Validates that the overall report status is Healthy when the only check is a healthy embedding check.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_OverallStatusHealthy_WhenEmbeddingCheckIsHealthy()
    {
        // Arrange
        SetupSuccessfulEmbedding(new[] { 0.1f });

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: 60000));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        Assert.AreEqual(HealthStatus.Healthy, report.Status);
    }

    #endregion

    #region Unhealthy - Time Exceeded

    /// <summary>
    /// Validates that when the response time exceeds the threshold,
    /// the health check entry reports Unhealthy status with the time exceeded error message.
    /// Uses a threshold of -1 to guarantee the threshold is always exceeded regardless of execution time.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_ReportsUnhealthy_WhenResponseTimeExceedsThreshold()
    {
        // Arrange
        SetupSuccessfulEmbedding(new[] { 0.1f });

        // Threshold of -1 guarantees any response time (>=0) will exceed the threshold.
        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: -1));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.AreEqual(HealthStatus.Unhealthy, embeddingCheck.Status);
        Assert.IsNotNull(embeddingCheck.Exception);
        Assert.IsTrue(embeddingCheck.Exception!.Contains(TIME_EXCEEDED_ERROR_MESSAGE));
    }

    /// <summary>
    /// Validates that the overall report status is Unhealthy when the embedding check is unhealthy.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_OverallStatusUnhealthy_WhenEmbeddingCheckIsUnhealthy()
    {
        // Arrange
        SetupSuccessfulEmbedding(new[] { 0.1f });

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: -1));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        Assert.AreEqual(HealthStatus.Unhealthy, report.Status);
    }

    #endregion

    #region Unhealthy - Dimensions Mismatch

    /// <summary>
    /// Validates that when the embedding dimensions don't match the expected value,
    /// the health check entry reports Unhealthy status with the dimensions mismatch error message.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_ReportsUnhealthy_WhenDimensionsMismatch()
    {
        // Arrange: Embedding returns 3 dimensions but config expects 5
        float[] embedding = new[] { 0.1f, 0.2f, 0.3f };
        SetupSuccessfulEmbedding(embedding);

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(
                enabled: true,
                thresholdMs: 60000,
                expectedDimensions: 5));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.AreEqual(HealthStatus.Unhealthy, embeddingCheck.Status);
        Assert.IsNotNull(embeddingCheck.Exception);
        Assert.IsTrue(embeddingCheck.Exception!.Contains(DIMENSIONS_MISMATCH_ERROR_MESSAGE));
        Assert.IsTrue(embeddingCheck.Exception.Contains("Expected: 5"));
        Assert.IsTrue(embeddingCheck.Exception.Contains("Actual: 3"));
        // Response time should still be recorded (not ERROR_RESPONSE_TIME_MS)
        Assert.IsTrue(embeddingCheck.ResponseTimeData!.ResponseTimeMs >= 0);
    }

    #endregion

    #region Unhealthy - Combined Failures

    /// <summary>
    /// Validates that when both dimensions mismatch and response time exceeds the threshold,
    /// the health check entry reports Unhealthy with both error messages combined.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_ReportsUnhealthy_WhenBothDimensionsMismatchAndTimeExceeded()
    {
        // Arrange: 3 dimensions, but expect 10; threshold of -1 guarantees time exceeded
        float[] embedding = new[] { 0.1f, 0.2f, 0.3f };
        SetupSuccessfulEmbedding(embedding);

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(
                enabled: true,
                thresholdMs: -1,
                expectedDimensions: 10));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.AreEqual(HealthStatus.Unhealthy, embeddingCheck.Status);
        Assert.IsNotNull(embeddingCheck.Exception);
        Assert.IsTrue(embeddingCheck.Exception!.Contains(DIMENSIONS_MISMATCH_ERROR_MESSAGE));
        Assert.IsTrue(embeddingCheck.Exception.Contains(TIME_EXCEEDED_ERROR_MESSAGE));
    }

    #endregion

    #region Unhealthy - Embedding Failure

    /// <summary>
    /// Validates that when the embedding service returns a failure with an error message,
    /// the health check entry reports Unhealthy with the error message and ERROR_RESPONSE_TIME_MS.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_ReportsUnhealthy_WhenEmbeddingFails()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(false, null, "Provider API returned 401 Unauthorized."));

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: 60000));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.AreEqual(HealthStatus.Unhealthy, embeddingCheck.Status);
        Assert.AreEqual("Provider API returned 401 Unauthorized.", embeddingCheck.Exception);
        Assert.AreEqual(HealthCheckConstants.ERROR_RESPONSE_TIME_MS, embeddingCheck.ResponseTimeData!.ResponseTimeMs);
    }

    /// <summary>
    /// Validates that when the embedding service returns a failure with no error message,
    /// the health check entry reports Unhealthy with the default "Embedding request failed." message.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_ReportsUnhealthy_WithDefaultErrorMessage_WhenNoErrorMessageProvided()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(false, null, null));

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: 60000));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.AreEqual(HealthStatus.Unhealthy, embeddingCheck.Status);
        Assert.AreEqual("Embedding request failed.", embeddingCheck.Exception);
        Assert.AreEqual(HealthCheckConstants.ERROR_RESPONSE_TIME_MS, embeddingCheck.ResponseTimeData!.ResponseTimeMs);
    }

    #endregion

    #region Unhealthy - Exception Handling

    /// <summary>
    /// Validates that when the embedding service throws an exception,
    /// the health check entry reports Unhealthy with the exception message and ERROR_RESPONSE_TIME_MS.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_ReportsUnhealthy_WhenExceptionThrown()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection timed out."));

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: 60000));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.AreEqual(HealthStatus.Unhealthy, embeddingCheck.Status);
        Assert.AreEqual("Connection timed out.", embeddingCheck.Exception);
        Assert.AreEqual(HealthCheckConstants.ERROR_RESPONSE_TIME_MS, embeddingCheck.ResponseTimeData!.ResponseTimeMs);
        CollectionAssert.Contains(embeddingCheck.Tags!, HealthCheckConstants.EMBEDDING);
    }

    #endregion

    #region Skip Scenarios

    /// <summary>
    /// Validates that when embeddings options are null,
    /// no embedding health check entry is added to the report.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_Skipped_WhenEmbeddingsOptionsNull()
    {
        // Arrange
        RuntimeConfig config = CreateRuntimeConfig(embeddingsOptions: null, embeddingsHealth: null);
        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        Assert.IsFalse(HasEmbeddingCheck(report));
    }

    /// <summary>
    /// Validates that when embeddings are disabled,
    /// no embedding health check entry is added to the report.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_Skipped_WhenEmbeddingsDisabled()
    {
        // Arrange
        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsEnabled: false,
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        Assert.IsFalse(HasEmbeddingCheck(report));
    }

    /// <summary>
    /// Validates that when the embeddings health check config is null,
    /// no embedding health check entry is added to the report.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_Skipped_WhenHealthConfigNull()
    {
        // Arrange
        RuntimeConfig config = CreateRuntimeConfig(embeddingsHealth: null);
        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        Assert.IsFalse(HasEmbeddingCheck(report));
    }

    /// <summary>
    /// Validates that when the embeddings health check is explicitly disabled,
    /// no embedding health check entry is added to the report.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_Skipped_WhenHealthCheckDisabled()
    {
        // Arrange
        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: false));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        Assert.IsFalse(HasEmbeddingCheck(report));
    }

    /// <summary>
    /// Validates that when the embedding service is null,
    /// no embedding health check entry is added to the report.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_Skipped_WhenEmbeddingServiceNull()
    {
        // Arrange
        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true));

        HealthCheckHelper helper = new(_mockLogger.Object, _httpUtilities, embeddingService: null);

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        Assert.IsFalse(HasEmbeddingCheck(report));
    }

    #endregion

    #region Test Text Validation

    /// <summary>
    /// Validates that the configured test text is passed to the embedding service.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_UsesConfiguredTestText()
    {
        // Arrange
        string customTestText = "custom health check text";
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(customTestText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, new[] { 0.1f }));

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(
                enabled: true,
                thresholdMs: 60000,
                testText: customTestText));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        await helper.GetHealthCheckResponseAsync(config);

        // Assert
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(customTestText, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <summary>
    /// Validates that the default test text is used when no custom test text is configured.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_UsesDefaultTestText_WhenNotConfigured()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(EmbeddingsHealthCheckConfig.DEFAULT_TEST_TEXT, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, new[] { 0.1f }));

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: 60000));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        await helper.GetHealthCheckResponseAsync(config);

        // Assert
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(EmbeddingsHealthCheckConfig.DEFAULT_TEST_TEXT, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    #endregion

    #region Tags Validation

    /// <summary>
    /// Validates that the embedding health check entry always includes the "embedding" tag.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_AlwaysIncludesEmbeddingTag()
    {
        // Arrange
        SetupSuccessfulEmbedding(new[] { 0.1f });

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: 60000));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        Assert.IsNotNull(embeddingCheck.Tags);
        Assert.AreEqual(1, embeddingCheck.Tags!.Count);
        Assert.AreEqual(HealthCheckConstants.EMBEDDING, embeddingCheck.Tags[0]);
    }

    /// <summary>
    /// Validates that even on failure, the embedding health check entry includes the "embedding" tag.
    /// </summary>
    [TestMethod]
    public async Task EmbeddingsHealthCheck_IncludesEmbeddingTag_OnFailure()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(false, null, "Error"));

        RuntimeConfig config = CreateRuntimeConfig(
            embeddingsHealth: new EmbeddingsHealthCheckConfig(enabled: true, thresholdMs: 60000));

        HealthCheckHelper helper = CreateHealthCheckHelper();

        // Act
        ComprehensiveHealthCheckReport report = await helper.GetHealthCheckResponseAsync(config);

        // Assert
        HealthCheckResultEntry embeddingCheck = GetEmbeddingCheck(report);
        CollectionAssert.Contains(embeddingCheck.Tags!, HealthCheckConstants.EMBEDDING);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Sets up the mock embedding service to return a successful result with the given embedding.
    /// </summary>
    private void SetupSuccessfulEmbedding(float[] embedding)
    {
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));
    }

    /// <summary>
    /// Creates a <see cref="HealthCheckHelper"/> using the class-level mocked dependencies.
    /// </summary>
    private HealthCheckHelper CreateHealthCheckHelper()
    {
        return new HealthCheckHelper(_mockLogger.Object, _httpUtilities, _mockEmbeddingService.Object);
    }

    /// <summary>
    /// Creates a <see cref="RuntimeConfig"/> with data source and entity health checks disabled
    /// to isolate embeddings health check behavior.
    /// </summary>
    /// <param name="embeddingsHealth">The embeddings health check config. Pass null to omit.</param>
    /// <param name="embeddingsOptions">Override the entire EmbeddingsOptions. When provided, embeddingsHealth and embeddingsEnabled are ignored.</param>
    /// <param name="embeddingsEnabled">Whether embeddings are enabled. Defaults to true.</param>
    private static RuntimeConfig CreateRuntimeConfig(
        EmbeddingsHealthCheckConfig? embeddingsHealth = null,
        EmbeddingsOptions? embeddingsOptions = null,
        bool embeddingsEnabled = true)
    {
        // If embeddingsOptions is not explicitly provided, build one from parameters
        if (embeddingsOptions is null && (embeddingsHealth is not null || embeddingsEnabled))
        {
            embeddingsOptions = new EmbeddingsOptions(
                Provider: EmbeddingProviderType.OpenAI,
                BaseUrl: "https://api.openai.com",
                ApiKey: "test-key",
                Enabled: embeddingsEnabled,
                Health: embeddingsHealth);
        }

        DataSource dataSource = new(
            DatabaseType.MSSQL,
            "Server=localhost;Database=test;",
            Options: null,
            Health: new DatasourceHealthCheckConfig(enabled: false));

        RuntimeOptions runtimeOptions = new(
            Rest: new RestRuntimeOptions(Enabled: true),
            GraphQL: new GraphQLRuntimeOptions(Enabled: true),
            Mcp: new McpRuntimeOptions(Enabled: true),
            Host: new HostOptions(Cors: null, Authentication: null, Mode: HostMode.Development),
            Health: new RuntimeHealthCheckConfig(enabled: true),
            Embeddings: embeddingsOptions);

        RuntimeEntities entities = new(new Dictionary<string, Entity>());

        return new RuntimeConfig(
            Schema: null,
            DataSource: dataSource,
            Entities: entities,
            Runtime: runtimeOptions);
    }

    /// <summary>
    /// Gets the embedding health check entry from the report, asserting it exists.
    /// </summary>
    private static HealthCheckResultEntry GetEmbeddingCheck(ComprehensiveHealthCheckReport report)
    {
        Assert.IsNotNull(report.Checks, "Checks should not be null.");
        HealthCheckResultEntry? embeddingCheck = report.Checks!
            .FirstOrDefault(c => c.Tags != null && c.Tags.Contains(HealthCheckConstants.EMBEDDING));
        Assert.IsNotNull(embeddingCheck, "Expected an embedding health check entry in the report.");
        return embeddingCheck!;
    }

    /// <summary>
    /// Checks if the report contains an embedding health check entry.
    /// </summary>
    private static bool HasEmbeddingCheck(ComprehensiveHealthCheckReport report)
    {
        return report.Checks != null &&
               report.Checks.Any(c => c.Tags != null && c.Tags.Contains(HealthCheckConstants.EMBEDDING));
    }

    #endregion
}

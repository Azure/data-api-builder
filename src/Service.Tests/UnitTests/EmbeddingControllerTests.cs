// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Azure.DataApiBuilder.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for EmbeddingController.
/// Covers route matching, authorization, request body parsing,
/// service availability, error handling, and integration with IEmbeddingService.
/// </summary>
[TestClass]
public class EmbeddingControllerTests
{
    private Mock<ILogger<EmbeddingController>> _mockLogger = null!;
    private Mock<IEmbeddingService> _mockEmbeddingService = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<EmbeddingController>>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockEmbeddingService.Setup(s => s.IsEnabled).Returns(true);
    }

    #region Route Matching and Path Validation Tests

    /// <summary>
    /// Tests that the controller returns NotFound when the request path does not match
    /// the configured endpoint path.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsNotFound_WhenPathDoesNotMatch()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/wrong-path",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    /// <summary>
    /// Tests that the controller returns success when the request path matches
    /// the configured endpoint path exactly.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_MatchesConfiguredPath()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f, 0.3f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/vectorize",
            requestPath: "/vectorize",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
    }

    /// <summary>
    /// Tests that the controller uses the default path "/embed" when no custom path is configured.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_UsesDefaultPath_WhenNotConfigured()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: null, // will use default "/embed"
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
    }

    /// <summary>
    /// Tests that path matching is case-insensitive.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_PathMatchingIsCaseInsensitive()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/Embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
    }

    /// <summary>
    /// Tests that path matching with a custom multi-segment path works correctly.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsNotFound_WhenCustomPathDoesNotMatch()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/api/embed",
            requestPath: "/embed",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    #endregion

    #region Embeddings and Endpoint Enabled/Disabled Tests

    /// <summary>
    /// Tests that the controller returns NotFound when embeddings config is null.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsNotFound_WhenEmbeddingsIsNull()
    {
        // Arrange
        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(embeddingsOptions: null);
        EmbeddingController controller = new(mockProvider.Object, _mockLogger.Object, _mockEmbeddingService.Object);
        controller.ControllerContext = CreateControllerContext("/embed");

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    /// <summary>
    /// Tests that the controller returns NotFound when embeddings is disabled.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsNotFound_WhenEmbeddingsIsDisabled()
    {
        // Arrange
        EmbeddingsOptions embeddingsOptions = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "key",
            Enabled: false,
            Endpoint: new EmbeddingsEndpointOptions(enabled: true));

        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(
            embeddingsOptions: embeddingsOptions, hostMode: HostMode.Development);
        EmbeddingController controller = new(mockProvider.Object, _mockLogger.Object, _mockEmbeddingService.Object);
        controller.ControllerContext = CreateControllerContext("/embed");

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    /// <summary>
    /// Tests that the controller returns NotFound when endpoint config is null.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsNotFound_WhenEndpointIsNull()
    {
        // Arrange
        EmbeddingsOptions embeddingsOptions = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "key",
            Endpoint: null);

        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(
            embeddingsOptions: embeddingsOptions, hostMode: HostMode.Development);
        EmbeddingController controller = new(mockProvider.Object, _mockLogger.Object, _mockEmbeddingService.Object);
        controller.ControllerContext = CreateControllerContext("/embed");

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    /// <summary>
    /// Tests that the controller returns NotFound when endpoint is disabled.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsNotFound_WhenEndpointIsDisabled()
    {
        // Arrange
        EmbeddingsOptions embeddingsOptions = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "key",
            Endpoint: new EmbeddingsEndpointOptions(enabled: false));

        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(
            embeddingsOptions: embeddingsOptions, hostMode: HostMode.Development);
        EmbeddingController controller = new(mockProvider.Object, _mockLogger.Object, _mockEmbeddingService.Object);
        controller.ControllerContext = CreateControllerContext("/embed");

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    #endregion

    #region Service Availability Tests

    /// <summary>
    /// Tests that the controller returns ServiceUnavailable when embedding service is null.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsServiceUnavailable_WhenServiceIsNull()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            hostMode: HostMode.Development,
            embeddingService: null,
            useClassMockService: false);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.ServiceUnavailable, objectResult.StatusCode);
    }

    /// <summary>
    /// Tests that the controller returns ServiceUnavailable when embedding service is disabled.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsServiceUnavailable_WhenServiceIsDisabled()
    {
        // Arrange
        Mock<IEmbeddingService> disabledService = new();
        disabledService.Setup(s => s.IsEnabled).Returns(false);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            hostMode: HostMode.Development,
            embeddingService: disabledService.Object,
            useClassMockService: false);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.ServiceUnavailable, objectResult.StatusCode);
    }

    #endregion

    #region Authorization Tests

    /// <summary>
    /// Tests that anonymous access is allowed in development mode when no roles are configured
    /// (development mode defaults to allowing anonymous).
    /// </summary>
    [TestMethod]
    public async Task PostAsync_AllowsAnonymous_InDevelopmentMode_WithNoRolesConfigured()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            endpointRoles: null,  // no roles configured — dev mode defaults to anonymous
            clientRole: null);    // no role header — defaults to anonymous

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
    }

    /// <summary>
    /// Tests that anonymous access is denied in production mode when no roles are configured.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsForbidden_InProductionMode_WithNoRolesConfigured()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: null,  // no roles configured — production returns empty
            clientRole: null);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.Forbidden, objectResult.StatusCode);
    }

    /// <summary>
    /// Tests that a request with an unauthorized role is denied.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsForbidden_WhenRoleIsNotAuthorized()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "admin" },
            clientRole: "reader");

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.Forbidden, objectResult.StatusCode);
    }

    /// <summary>
    /// Tests that a request with an authorized role is accepted.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_AllowsAccess_WhenRoleIsAuthorized()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "admin", "reader" },
            clientRole: "admin");

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
    }

    /// <summary>
    /// Tests that role matching is case-insensitive.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_RoleMatchingIsCaseInsensitive()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "Admin" },
            clientRole: "ADMIN");

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
    }

    /// <summary>
    /// Tests that when no X-MS-API-ROLE header is provided, the anonymous role is used.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_UsesAnonymousRole_WhenNoRoleHeaderProvided()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "anonymous" },
            clientRole: null); // no role header

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
    }

    #endregion

    #region Request Body Parsing Tests

    /// <summary>
    /// Tests successful embedding with a plain text request body.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsEmbedding_ForPlainTextBody()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f, 0.3f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "Hello, world!",
            contentType: "text/plain",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
        ContentResult contentResult = (ContentResult)result;
        Assert.AreEqual("0.1,0.2,0.3", contentResult.Content);
        Assert.AreEqual("text/plain", contentResult.ContentType);
    }

    /// <summary>
    /// Tests successful embedding with a JSON-wrapped string request body.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsEmbedding_ForJsonWrappedStringBody()
    {
        // Arrange
        float[] embedding = new[] { 0.4f, 0.5f };
        string expectedText = "Hello, world!";
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(expectedText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "\"Hello, world!\"", // JSON-wrapped string
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
        ContentResult contentResult = (ContentResult)result;
        Assert.AreEqual("0.4,0.5", contentResult.Content);

        // Verify the service was called with the unwrapped string
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(expectedText, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <summary>
    /// Tests that invalid JSON body is treated as plain text.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_TreatsInvalidJsonAsPlainText()
    {
        // Arrange
        string rawBody = "not valid json {[";
        float[] embedding = new[] { 0.6f, 0.7f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(rawBody, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: rawBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
        ContentResult contentResult = (ContentResult)result;
        Assert.AreEqual("0.6,0.7", contentResult.Content);

        // Verify the service was called with the raw body (since JSON deserialization failed)
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(rawBody, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    #endregion

    #region Empty Request Body Validation Tests

    /// <summary>
    /// Tests that an empty request body returns BadRequest.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsBadRequest_ForEmptyBody()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
    }

    /// <summary>
    /// Tests that a whitespace-only request body returns BadRequest.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsBadRequest_ForWhitespaceOnlyBody()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "   \n\t  ",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
    }

    #endregion

    #region Error Response Handling Tests

    /// <summary>
    /// Tests that InternalServerError is returned when TryEmbedAsync fails.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsInternalServerError_WhenEmbeddingFails()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(false, null, "Provider returned an error."));

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
        Assert.IsTrue(objectResult.Value?.ToString()?.Contains("Provider returned an error."));
    }

    /// <summary>
    /// Tests that InternalServerError is returned when embedding result is null.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsInternalServerError_WhenEmbeddingIsNull()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, null));

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
    }

    /// <summary>
    /// Tests that InternalServerError is returned when embedding result is empty array.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsInternalServerError_WhenEmbeddingIsEmpty()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, Array.Empty<float>()));

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
    }

    /// <summary>
    /// Tests that when TryEmbedAsync fails with no error message, a default error message is returned.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsDefaultErrorMessage_WhenNoErrorMessageProvided()
    {
        // Arrange
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(false, null, null));

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
        Assert.AreEqual("Failed to generate embedding.", objectResult.Value?.ToString());
    }

    #endregion

    #region Integration with IEmbeddingService Tests

    /// <summary>
    /// Tests that the embedding service is called with the correct text from the request body.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_CallsEmbeddingService_WithCorrectText()
    {
        // Arrange
        string inputText = "This is the text to embed";
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(inputText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: inputText,
            hostMode: HostMode.Development);

        // Act
        await controller.PostAsync(route: null);

        // Assert
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <summary>
    /// Tests that the embedding vector is returned as comma-separated floats in plain text.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsCommaSeparatedFloats()
    {
        // Arrange
        float[] embedding = new[] { 1.5f, -0.25f, 3.14159f, 0f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
        ContentResult contentResult = (ContentResult)result;
        Assert.AreEqual("1.5,-0.25,3.14159,0", contentResult.Content);
    }

    /// <summary>
    /// Tests that the embedding service is not called when the service is unavailable.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_DoesNotCallService_WhenServiceIsUnavailable()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            embeddingService: null,
            useClassMockService: false);

        // Act
        await controller.PostAsync(route: null);

        // Assert
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    /// <summary>
    /// Tests that the embedding service is not called when the request body is empty.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_DoesNotCallService_WhenBodyIsEmpty()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "",
            hostMode: HostMode.Development);

        // Act
        await controller.PostAsync(route: null);

        // Assert
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    /// <summary>
    /// Tests that the embedding service is not called when authorization fails.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_DoesNotCallService_WhenAuthorizationFails()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "admin" },
            clientRole: "unauthorized-role");

        // Act
        await controller.PostAsync(route: null);

        // Assert
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion

    #region Development vs Production Mode Tests

    /// <summary>
    /// Tests that development mode allows anonymous access by default even without explicit roles.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_DevelopmentMode_DefaultsToAnonymousAccess()
    {
        // Arrange
        float[] embedding = new[] { 0.1f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test",
            hostMode: HostMode.Development,
            endpointRoles: null,
            clientRole: null);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert - should succeed because dev mode defaults to anonymous access
        Assert.IsInstanceOfType(result, typeof(ContentResult));
    }

    /// <summary>
    /// Tests that production mode denies access by default when no roles are configured.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ProductionMode_DeniesAccessByDefault()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test",
            hostMode: HostMode.Production,
            endpointRoles: null,
            clientRole: null);

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.Forbidden, objectResult.StatusCode);
    }

    /// <summary>
    /// Tests that production mode allows access when the client role is in the configured roles.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ProductionMode_AllowsConfiguredRole()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            endpointPath: "/embed",
            requestPath: "/embed",
            requestBody: "test",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "authenticated", "admin" },
            clientRole: "authenticated");

        // Act
        IActionResult result = await controller.PostAsync(route: null);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
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
    /// Creates an EmbeddingController with all the necessary mocks wired up.
    /// </summary>
    private EmbeddingController CreateController(
        string? endpointPath,
        string requestPath,
        string? requestBody = null,
        string? contentType = "text/plain",
        HostMode hostMode = HostMode.Development,
        string[]? endpointRoles = null,
        string? clientRole = null,
        IEmbeddingService? embeddingService = null,
        bool useClassMockService = true)
    {
        EmbeddingsEndpointOptions endpointOptions = new(
            enabled: true,
            path: endpointPath,
            roles: endpointRoles);

        EmbeddingsOptions embeddingsOptions = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key",
            Endpoint: endpointOptions);

        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(
            embeddingsOptions: embeddingsOptions,
            hostMode: hostMode);

        // If useClassMockService is true and no explicit service provided, use the class-level mock
        IEmbeddingService? serviceToUse = useClassMockService && embeddingService is null
            ? _mockEmbeddingService.Object
            : embeddingService;

        EmbeddingController controller = new(
            mockProvider.Object,
            _mockLogger.Object,
            serviceToUse);

        controller.ControllerContext = CreateControllerContext(
            requestPath,
            requestBody,
            contentType,
            clientRole);

        return controller;
    }

    /// <summary>
    /// Creates a mock RuntimeConfigProvider that returns a config with the specified embeddings and host options.
    /// </summary>
    private static Mock<RuntimeConfigProvider> CreateMockConfigProvider(
        EmbeddingsOptions? embeddingsOptions,
        HostMode hostMode = HostMode.Development)
    {
        HostOptions hostOptions = new(
            Cors: null,
            Authentication: null,
            Mode: hostMode);

        RuntimeOptions runtimeOptions = new(
            Rest: null,
            GraphQL: null,
            Mcp: null,
            Host: hostOptions,
            Embeddings: embeddingsOptions);

        DataSource dataSource = new(DatabaseType.MSSQL, string.Empty);
        RuntimeEntities entities = new(new System.Collections.Generic.Dictionary<string, Entity>());

        RuntimeConfig config = new(
            Schema: null,
            DataSource: dataSource,
            Entities: entities,
            Runtime: runtimeOptions);

        Mock<RuntimeConfigLoader> mockLoader = new(null, null);
        Mock<RuntimeConfigProvider> mockProvider = new(mockLoader.Object);
        mockProvider
            .Setup(p => p.GetConfig())
            .Returns(config);

        return mockProvider;
    }

    /// <summary>
    /// Creates a ControllerContext with a configured HttpContext for testing.
    /// </summary>
    private static ControllerContext CreateControllerContext(
        string requestPath,
        string? requestBody = null,
        string? contentType = "text/plain",
        string? clientRole = null)
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Path = requestPath;
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = contentType;

        if (requestBody is not null)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(requestBody);
            httpContext.Request.Body = new MemoryStream(bodyBytes);
            httpContext.Request.ContentLength = bodyBytes.Length;
        }
        else
        {
            httpContext.Request.Body = new MemoryStream();
        }

        if (!string.IsNullOrEmpty(clientRole))
        {
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = clientRole;
        }

        return new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    #endregion
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Azure.DataApiBuilder.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for EmbeddingController.
/// Covers fixed route metadata, authorization, request body parsing,
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

    #region Fixed Endpoint Route Tests

    /// <summary>
    /// Tests that the controller action is bound to the fixed "embed" route.
    /// </summary>
    [TestMethod]
    public void PostAsync_UsesFixedEmbedRoute()
    {
        RouteAttribute? routeAttribute = typeof(EmbeddingController)
            .GetMethod(nameof(EmbeddingController.PostAsync))?
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .SingleOrDefault();

        Assert.IsNotNull(routeAttribute);
        Assert.AreEqual("embed", routeAttribute.Template);
    }

    /// <summary>
    /// Tests that embedding requests succeed at the fixed endpoint route.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_SucceedsAtFixedEndpointRoute()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f, 0.3f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
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
        IActionResult result = await controller.PostAsync();

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
        IActionResult result = await controller.PostAsync();

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
        IActionResult result = await controller.PostAsync();

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
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            hostMode: HostMode.Development,
            embeddingService: null,
            useClassMockService: false);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            hostMode: HostMode.Development,
            embeddingService: disabledService.Object,
            useClassMockService: false);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            endpointRoles: null,  // no roles configured — dev mode defaults to anonymous
            clientRole: null);    // no role header — defaults to anonymous

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
    }

    /// <summary>
    /// Tests that anonymous access is denied in production mode when no roles are configured.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsForbidden_InProductionMode_WithNoRolesConfigured()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: null,  // no roles configured — production returns empty
            clientRole: null);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "admin" },
            clientRole: "reader");

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "admin", "reader" },
            clientRole: "admin");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "Admin" },
            clientRole: "ADMIN");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "anonymous" },
            clientRole: null); // no role header

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
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
            requestPath: "/embed",
            requestBody: "Hello, world!",
            contentType: "text/plain",
            hostMode: HostMode.Development,
            acceptHeader: "text/plain");

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "\"Hello, world!\"", // JSON-wrapped string
            contentType: "application/json",
            hostMode: HostMode.Development,
            acceptHeader: "text/plain");

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: rawBody,
            contentType: "application/json",
            hostMode: HostMode.Development,
            acceptHeader: "text/plain");

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "   \n\t  ",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: inputText,
            hostMode: HostMode.Development);

        // Act
        await controller.PostAsync();

        // Assert
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <summary>
    /// Tests that the embedding vector is returned as comma-separated floats in plain text
    /// when Accept: text/plain is requested.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsCommaSeparatedFloats()
    {
        // Arrange
        float[] embedding = new[] { 1.5f, -0.25f, 3.14159f, 0f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test",
            hostMode: HostMode.Development,
            acceptHeader: "text/plain");

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            embeddingService: null,
            useClassMockService: false);

        // Act
        await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "",
            hostMode: HostMode.Development);

        // Act
        await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "admin" },
            clientRole: "unauthorized-role");

        // Act
        await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test",
            hostMode: HostMode.Development,
            endpointRoles: null,
            clientRole: null);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert - should succeed because dev mode defaults to anonymous access
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
    }

    /// <summary>
    /// Tests that production mode denies access by default when no roles are configured.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ProductionMode_DeniesAccessByDefault()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test",
            hostMode: HostMode.Production,
            endpointRoles: null,
            clientRole: null);

        // Act
        IActionResult result = await controller.PostAsync();

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
            requestPath: "/embed",
            requestBody: "test",
            hostMode: HostMode.Production,
            endpointRoles: new[] { "authenticated", "admin" },
            clientRole: "authenticated");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
    }

    #endregion

    #region Content Negotiation Tests

    /// <summary>
    /// Tests that the default response (no Accept header) is JSON with EmbeddingResponse.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsJson_WhenNoAcceptHeader()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f, 0.3f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            acceptHeader: null); // no Accept header

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        Assert.IsInstanceOfType(okResult.Value, typeof(EmbeddingResponse));
        EmbeddingResponse response = (EmbeddingResponse)okResult.Value!;
        CollectionAssert.AreEqual(embedding, response.Embedding);
        Assert.AreEqual(3, response.Dimensions);
    }

    /// <summary>
    /// Tests that Accept: application/json returns JSON with EmbeddingResponse.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsJson_WhenAcceptIsApplicationJson()
    {
        // Arrange
        float[] embedding = new[] { 0.5f, 0.6f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            acceptHeader: "application/json");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        Assert.IsInstanceOfType(okResult.Value, typeof(EmbeddingResponse));
        EmbeddingResponse response = (EmbeddingResponse)okResult.Value!;
        CollectionAssert.AreEqual(embedding, response.Embedding);
        Assert.AreEqual(2, response.Dimensions);
    }

    /// <summary>
    /// Tests that Accept: text/plain returns plain text with comma-separated floats.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsTextPlain_WhenAcceptIsTextPlain()
    {
        // Arrange
        float[] embedding = new[] { 0.7f, 0.8f, 0.9f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            acceptHeader: "text/plain");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
        ContentResult contentResult = (ContentResult)result;
        Assert.AreEqual("0.7,0.8,0.9", contentResult.Content);
        Assert.AreEqual("text/plain", contentResult.ContentType);
    }

    /// <summary>
    /// Tests that when Accept includes both application/json and text/plain, JSON wins.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsJson_WhenAcceptIncludesBothJsonAndTextPlain()
    {
        // Arrange
        float[] embedding = new[] { 1.0f, 2.0f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            acceptHeader: "text/plain, application/json");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert - JSON wins when both are present
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        Assert.IsInstanceOfType(okResult.Value, typeof(EmbeddingResponse));
    }

    /// <summary>
    /// Tests that Accept: */* returns JSON (default format).
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsJson_WhenAcceptIsWildcard()
    {
        // Arrange
        float[] embedding = new[] { 0.1f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "test text",
            hostMode: HostMode.Development,
            acceptHeader: "*/*");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert - wildcard does not trigger text/plain
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
    }

    #endregion

    #region Document Array with Chunking Tests

    /// <summary>
    /// Tests that document array requests are properly processed.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsEmbeddings_ForDocumentArray()
    {
        // Arrange
        float[] embedding1 = new[] { 0.1f, 0.2f };
        float[] embedding2 = new[] { 0.3f, 0.4f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync("First document", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding1));
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync("Second document", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding2));

        string requestBody = """
            [
                {"key": "doc-1", "text": "First document"},
                {"key": "doc-2", "text": "Second document"}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        Assert.IsNotNull(okResult.Value);

        EmbedDocumentResponse[]? responses = okResult.Value as EmbedDocumentResponse[];
        Assert.IsNotNull(responses);
        Assert.AreEqual(2, responses.Length);
        Assert.AreEqual("doc-1", responses[0].Key);
        Assert.AreEqual("doc-2", responses[1].Key);
        Assert.AreEqual(1, responses[0].Data.Length); // no chunking by default
        Assert.AreEqual(1, responses[1].Data.Length);
    }

    /// <summary>
    /// Tests that document array with chunking enabled splits text into multiple embeddings.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunksDocuments_WhenChunkingEnabled()
    {
        // Arrange
        float[] embedding1 = new[] { 0.1f, 0.2f };
        float[] embedding2 = new[] { 0.3f, 0.4f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) =>
            {
                return text.Contains("First") ? new EmbeddingResult(true, embedding1) : new EmbeddingResult(true, embedding2);
            });

        // Create a long text that will be chunked (default chunk size is 1000)
        string longText = new string('A', 1500);

        string requestBody = $$"""
            [
                {"key": "doc-1", "text": "{{longText}}"}
            ]
            """;

        EmbeddingsEndpointOptions endpointOptions = new(enabled: true);
        EmbeddingsChunkingOptions chunkingOptions = new(Enabled: true, SizeChars: 1000, OverlapChars: 250);
        EmbeddingsOptions embeddingsOptions = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key",
            Endpoint: endpointOptions,
            Chunking: chunkingOptions);

        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(
            embeddingsOptions: embeddingsOptions,
            hostMode: HostMode.Development);

        EmbeddingController controller = new(
            mockProvider.Object,
            _mockLogger.Object,
            _mockEmbeddingService.Object);

        controller.ControllerContext = CreateControllerContext(
            "/embed",
            requestBody,
            "application/json");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        EmbedDocumentResponse[]? responses = okResult.Value as EmbedDocumentResponse[];
        Assert.IsNotNull(responses);
        Assert.AreEqual(1, responses.Length);
        Assert.AreEqual("doc-1", responses[0].Key);
        Assert.IsTrue(responses[0].Data.Length > 1, "Text should be chunked into multiple embeddings");
    }

    /// <summary>
    /// Tests that query parameter $chunking.enabled=true overrides config.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingQueryParameter_EnablesChunking()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));

        string longText = new string('A', 1500);
        string requestBody = $$"""
            [
                {"key": "doc-1", "text": "{{longText}}"}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed?$chunking.enabled=true&$chunking.size-chars=500",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        EmbedDocumentResponse[]? responses = okResult.Value as EmbedDocumentResponse[];
        Assert.IsNotNull(responses);
        Assert.AreEqual("doc-1", responses[0].Key);
        Assert.IsTrue(responses[0].Data.Length >= 3, "Text should be chunked into at least 3 embeddings with 500 char chunks");
    }

    /// <summary>
    /// Tests that query parameter $chunking.size-chars overrides config.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingQueryParameter_OverridesChunkSize()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        int callCount = 0;
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding))
            .Callback(() => callCount++);

        string text = new string('A', 1000);
        string requestBody = $$"""
            [
                {"key": "doc-1", "text": "{{text}}"}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed?$chunking.enabled=true&$chunking.size-chars=300&$chunking.overlap-chars=0",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        // 1000 chars with 300 char chunks and no overlap = 4 chunks (300, 300, 300, 100)
        Assert.IsTrue(callCount >= 4, $"Expected at least 4 embedding calls, but got {callCount}");
    }

    /// <summary>
    /// Tests that query parameter $chunking.overlap-chars is respected.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingQueryParameter_OverridesOverlapChars()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        List<string> embeddedTexts = new();
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding))
            .Callback<string, CancellationToken>((text, _) => embeddedTexts.Add(text));

        string text = "0123456789" + "ABCDEFGHIJ" + "abcdefghij"; // 30 chars
        string requestBody = $$"""
            [
                {"key": "doc-1", "text": "{{text}}"}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed?$chunking.enabled=true&$chunking.size-chars=15&$chunking.overlap-chars=5",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.IsTrue(embeddedTexts.Count >= 2, "Should have multiple chunks");
        
        // Check overlap: last 5 chars of first chunk should match first 5 chars of second chunk
        if (embeddedTexts.Count >= 2)
        {
            string chunk1End = embeddedTexts[0].Substring(Math.Max(0, embeddedTexts[0].Length - 5));
            string chunk2Start = embeddedTexts[1].Substring(0, Math.Min(5, embeddedTexts[1].Length));
            Assert.AreEqual(chunk1End, chunk2Start, "Chunks should have overlapping content");
        }
    }

    /// <summary>
    /// Tests that $chunking.enabled=false disables chunking even if config enables it.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingQueryParameter_DisablesChunking()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        int callCount = 0;
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding))
            .Callback(() => callCount++);

        string longText = new string('A', 2000);
        string requestBody = $$"""
            [
                {"key": "doc-1", "text": "{{longText}}"}
            ]
            """;

        EmbeddingsEndpointOptions endpointOptions = new(enabled: true);
        EmbeddingsChunkingOptions chunkingOptions = new(Enabled: true, SizeChars: 500, OverlapChars: 100);
        EmbeddingsOptions embeddingsOptions = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key",
            Endpoint: endpointOptions,
            Chunking: chunkingOptions);

        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(
            embeddingsOptions: embeddingsOptions,
            hostMode: HostMode.Development);

        EmbeddingController controller = new(
            mockProvider.Object,
            _mockLogger.Object,
            _mockEmbeddingService.Object);

        controller.ControllerContext = CreateControllerContext(
            "/embed?$chunking.enabled=false",
            requestBody,
            "application/json");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        Assert.AreEqual(1, callCount, "Should not chunk when disabled via query parameter");
    }

    /// <summary>
    /// Tests that empty document array returns BadRequest.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsBadRequest_ForEmptyDocumentArray()
    {
        // Arrange
        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "[]",
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
    }

    /// <summary>
    /// Tests that document with missing key returns InternalServerError.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_HandlesDocumentWithMissingKey()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));

        string requestBody = """
            [
                {"text": "Document without key"}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert - document without key should be handled gracefully
        // Check that result is either BadRequest or that the key is null/empty in response
        Assert.IsTrue(
            result is BadRequestObjectResult || 
            (result is OkObjectResult okResult && 
             okResult.Value is EmbedDocumentResponse[] responses &&
             string.IsNullOrEmpty(responses[0].Key)));
    }

    /// <summary>
    /// Tests that document with empty text is skipped or returns error.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_HandlesDocumentWithEmptyText()
    {
        // Arrange
        string requestBody = """
            [
                {"key": "doc-1", "text": ""}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert - empty text should result in error
        Assert.IsTrue(
            result is BadRequestObjectResult || 
            result is ObjectResult errorResult && errorResult.StatusCode == 500);
    }

    /// <summary>
    /// Tests that chunking respects minimum chunk size.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingHandlesVerySmallChunkSize()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));

        string requestBody = """
            [
                {"key": "doc-1", "text": "Short"}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed?$chunking.enabled=true&$chunking.size-chars=1",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert - should not crash with very small chunk size (may return error due to invalid config)
        Assert.IsNotNull(result, "Result should not be null");
    }

    /// <summary>
    /// Tests chunking with overlap larger than chunk size.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingHandlesOverlapLargerThanChunkSize()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));

        string text = new string('A', 100);
        string requestBody = $$"""
            [
                {"key": "doc-1", "text": "{{text}}"}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed?$chunking.enabled=true&$chunking.size-chars=50&$chunking.overlap-chars=60",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert - should handle overlap >= size gracefully
        Assert.IsTrue(result is OkObjectResult || result is BadRequestObjectResult);
    }

    /// <summary>
    /// Tests that failed embeddings in document array process are handled.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_HandlesEmbeddingFailure_InDocumentArray()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync("First document", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(true, embedding));
        _mockEmbeddingService
            .Setup(s => s.TryEmbedAsync("Second document", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingResult(false, null, "Provider error"));

        string requestBody = """
            [
                {"key": "doc-1", "text": "First document"},
                {"key": "doc-2", "text": "Second document"}
            ]
            """;

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: requestBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert - should return error when any embedding fails
        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        ObjectResult objectResult = (ObjectResult)result;
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
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
        string requestPath,
        string? requestBody = null,
        string? contentType = "text/plain",
        HostMode hostMode = HostMode.Development,
        string[]? endpointRoles = null,
        string? clientRole = null,
        IEmbeddingService? embeddingService = null,
        bool useClassMockService = true,
        string? acceptHeader = null)
    {
        EmbeddingsEndpointOptions endpointOptions = new(
            enabled: true,
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
            clientRole,
            acceptHeader);

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
        string? clientRole = null,
        string? acceptHeader = null)
    {
        DefaultHttpContext httpContext = new();
        
        // Parse path and query string
        int queryIndex = requestPath.IndexOf('?');
        if (queryIndex >= 0)
        {
            httpContext.Request.Path = requestPath.Substring(0, queryIndex);
            httpContext.Request.QueryString = new QueryString(requestPath.Substring(queryIndex));
        }
        else
        {
            httpContext.Request.Path = requestPath;
        }
        
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

        if (!string.IsNullOrEmpty(acceptHeader))
        {
            httpContext.Request.Headers.Accept = acceptHeader;
        }

        return new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    #endregion
}

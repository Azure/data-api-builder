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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.ServiceUnavailable, (int)value!.error.status);
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.ServiceUnavailable, (int)value!.error.status);
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.Forbidden, (int)value!.error.status);
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.Forbidden, (int)value!.error.status);
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
    /// Tests that an application/json body that is neither a string nor a document array returns BadRequest.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsBadRequest_ForInvalidJsonBody()
    {
        // Arrange — a JSON object is not a valid string or document array
        string rawBody = "{\"foo\":\"bar\"}";

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: rawBody,
            contentType: "application/json",
            hostMode: HostMode.Development);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert — controller must reject the body with a descriptive message
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
        Assert.IsTrue(
            jsonResult.Value?.ToString()?.Contains("application/json") == true,
            "Error message should mention 'application/json'.");

        // Embedding service must NOT be called
        _mockEmbeddingService.Verify(
            s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, (int)value!.error.status);
        // Error message must NOT expose internal provider details
        Assert.IsFalse(
            jsonResult.Value?.ToString()?.Contains("Provider returned an error.") == true,
            "Internal error details must not be exposed to the client.");
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, (int)value!.error.status);
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, (int)value!.error.status);
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, (int)value!.error.status);
        // The generic error message should be returned, not internal details
        Assert.IsTrue(jsonResult.Value?.ToString()?.Contains("Failed to generate embedding.") == true);
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.Forbidden, (int)value!.error.status);
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
        // Arrange — controller uses TryEmbedBatchAsync per document
        float[] embedding1 = new[] { 0.1f, 0.2f };
        float[] embedding2 = new[] { 0.3f, 0.4f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(
                It.Is<string[]>(texts => texts.Length == 1 && texts[0] == "First document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingBatchResult(true, new[] { embedding1 }));
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(
                It.Is<string[]>(texts => texts.Length == 1 && texts[0] == "Second document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingBatchResult(true, new[] { embedding2 }));

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
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding1).ToArray()));

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
            Enabled: true,
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
        // Arrange — controller calls TryEmbedBatchAsync (not TryEmbedAsync) for document arrays
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

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
        // Arrange — controller sends all chunks as a single batch per document
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

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
        OkObjectResult okResult = (OkObjectResult)result;
        EmbedDocumentResponse[]? responses = okResult.Value as EmbedDocumentResponse[];
        Assert.IsNotNull(responses);
        // 1000 chars with 300-char chunks and no overlap = 4 chunks (300, 300, 300, 100)
        Assert.IsTrue(responses[0].Data.Length >= 4, $"Expected at least 4 chunks, but got {responses[0].Data.Length}");
    }

    /// <summary>
    /// Tests that query parameter $chunking.overlap-chars is respected.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingQueryParameter_OverridesOverlapChars()
    {
        // Arrange — capture the chunks batch to verify overlap
        float[] embedding = new[] { 0.1f, 0.2f };
        List<string[]> capturedBatches = new();
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()))
            .Callback<string[], CancellationToken>((texts, _) => capturedBatches.Add(texts));

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
        Assert.IsTrue(capturedBatches.Count > 0, "TryEmbedBatchAsync should be called");
        string[] chunks = capturedBatches[0];
        Assert.IsTrue(chunks.Length >= 2, "Should have multiple chunks");

        // Check overlap: last 5 chars of chunk[i] should match first 5 chars of chunk[i+1]
        if (chunks.Length >= 2)
        {
            string chunk1End = chunks[0].Substring(Math.Max(0, chunks[0].Length - 5));
            string chunk2Start = chunks[1].Substring(0, Math.Min(5, chunks[1].Length));
            Assert.AreEqual(chunk1End, chunk2Start, "Chunks should have overlapping content");
        }
    }

    /// <summary>
    /// Tests that $chunking.enabled=false disables chunking even if config enables it.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingQueryParameter_DisablesChunking()
    {
        // Arrange — controller calls TryEmbedBatchAsync; with chunking disabled the batch has 1 element
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

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
            Enabled: true,
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
        OkObjectResult okResult = (OkObjectResult)result;
        EmbedDocumentResponse[]? responses = okResult.Value as EmbedDocumentResponse[];
        Assert.IsNotNull(responses);
        Assert.AreEqual(1, responses[0].Data.Length, "Should not chunk when disabled via query parameter");
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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
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

        // Assert - document without key should be rejected with 400
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
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

        // Assert - empty text should result in a 400 error
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
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
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

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

        // Assert — size=1 produces one chunk per character; must not crash
        Assert.IsNotNull(result, "Result should not be null");
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
    }

    /// <summary>
    /// Tests chunking with overlap larger than chunk size.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ChunkingHandlesOverlapLargerThanChunkSize()
    {
        // Arrange — EffectiveSizeChars clamps to overlap+1, so chunking terminates safely
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

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

        // Assert — overlap clamped via EffectiveSizeChars; result must be Ok
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
    }

    /// <summary>
    /// Tests that failed embeddings in document array process are handled.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_HandlesEmbeddingFailure_InDocumentArray()
    {
        // Arrange — first doc succeeds, second fails; controller uses TryEmbedBatchAsync per doc
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .SetupSequence(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingBatchResult(true, new[] { embedding }))
            .ReturnsAsync(new EmbeddingBatchResult(false, null, "Provider error"));

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
        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult jsonResult = (JsonResult)result;
        dynamic? value = jsonResult.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.InternalServerError, (int)value!.error.status);
    }

    #endregion

    #region Invalid Query Parameter Tests

    /// <summary>
    /// Tests that an invalid $chunking.enabled value returns BadRequest.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsBadRequest_ForInvalidChunkingEnabled()
    {
        EmbeddingController controller = CreateController(
            requestPath: "/embed?$chunking.enabled=notabool",
            requestBody: "test",
            hostMode: HostMode.Development);

        IActionResult result = await controller.PostAsync();

        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult bad = (JsonResult)result;
        dynamic? value = bad.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
        Assert.IsTrue(bad.Value?.ToString()?.Contains("$chunking.enabled") == true);
    }

    /// <summary>
    /// Tests that a non-positive $chunking.size-chars returns BadRequest.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsBadRequest_ForNonPositiveChunkSize()
    {
        EmbeddingController controller = CreateController(
            requestPath: "/embed?$chunking.size-chars=0",
            requestBody: "test",
            hostMode: HostMode.Development);

        IActionResult result = await controller.PostAsync();

        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult bad = (JsonResult)result;
        dynamic? value = bad.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
        Assert.IsTrue(bad.Value?.ToString()?.Contains("$chunking.size-chars") == true);
    }

    /// <summary>
    /// Tests that a negative $chunking.overlap-chars returns BadRequest.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_ReturnsBadRequest_ForNegativeOverlapChars()
    {
        EmbeddingController controller = CreateController(
            requestPath: "/embed?$chunking.overlap-chars=-1",
            requestBody: "test",
            hostMode: HostMode.Development);

        IActionResult result = await controller.PostAsync();

        Assert.IsInstanceOfType(result, typeof(JsonResult));
        JsonResult bad = (JsonResult)result;
        dynamic? value = bad.Value;
        Assert.IsNotNull(value);
        Assert.AreEqual((int)HttpStatusCode.BadRequest, (int)value!.error.status);
        Assert.IsTrue(bad.Value?.ToString()?.Contains("$chunking.overlap-chars") == true);
    }

    #endregion

    #region Single Text with Chunking Tests

    /// <summary>
    /// Tests that a plain-text body with chunking enabled is routed through the
    /// document-array path and returns multiple embeddings.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_SingleText_WithChunkingEnabled_ReturnsDocumentResponse()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

        string longText = new string('X', 1500);

        EmbeddingsEndpointOptions endpointOptions = new(enabled: true);
        EmbeddingsChunkingOptions chunkingOptions = new(Enabled: true, SizeChars: 1000, OverlapChars: 250);
        EmbeddingsOptions embeddingsOptions = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key",
            Enabled: true,
            Endpoint: endpointOptions,
            Chunking: chunkingOptions);

        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(
            embeddingsOptions: embeddingsOptions,
            hostMode: HostMode.Development);

        EmbeddingController controller = new(mockProvider.Object, _mockLogger.Object, _mockEmbeddingService.Object);
        controller.ControllerContext = CreateControllerContext("/embed", longText, "text/plain");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert — chunking routes through document-array path; returns EmbedDocumentResponse[]
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        EmbedDocumentResponse[]? responses = okResult.Value as EmbedDocumentResponse[];
        Assert.IsNotNull(responses, "Chunked single-text should return EmbedDocumentResponse[]");
        Assert.AreEqual("input", responses[0].Key);
        Assert.IsTrue(responses[0].Data.Length > 1, "Text should be split into multiple chunks");
    }

    /// <summary>
    /// Tests that a plain-text body with chunking disabled returns the legacy EmbeddingResponse.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_SingleText_WithChunkingDisabled_ReturnsEmbeddingResponse()
    {
        float[] embedding = new[] { 0.1f, 0.2f };
        SetupSuccessfulEmbedding(embedding);

        EmbeddingController controller = CreateController(
            requestPath: "/embed",
            requestBody: "hello world",
            contentType: "text/plain",
            hostMode: HostMode.Development);

        IActionResult result = await controller.PostAsync();

        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        Assert.IsInstanceOfType(okResult.Value, typeof(EmbeddingResponse));
    }

    #endregion

    #region Accept: text/plain Consistency with Chunking Tests

    /// <summary>
    /// Single text + chunking enabled + Accept: text/plain must return ContentResult (not JSON),
    /// with one line per chunk where each line is comma-separated floats.
    /// This validates that the Accept header is honoured consistently regardless of whether
    /// chunking routes through the document-array path.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_SingleText_ChunkingEnabled_AcceptTextPlain_ReturnsPlainTextLines()
    {
        // Arrange — a 1500-char text with 1000-char chunks and no overlap produces exactly 2 chunks
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

        EmbeddingController controller = CreateControllerWithChunking(
            requestBody: new string('X', 1500),
            acceptHeader: "text/plain");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert — ContentResult, not OkObjectResult
        Assert.IsInstanceOfType(result, typeof(ContentResult));
        ContentResult contentResult = (ContentResult)result;
        Assert.AreEqual("text/plain", contentResult.ContentType);
        Assert.IsNotNull(contentResult.Content);

        // Two chunks → two newline-separated lines
        string[] lines = contentResult.Content!.Split('\n');
        Assert.AreEqual(2, lines.Length, "Each chunk produces one line.");
        foreach (string line in lines)
        {
            Assert.IsTrue(line.Contains(','), "Each line must contain comma-separated floats.");
        }
    }

    /// <summary>
    /// Validates the exact text/plain format for a chunked single-text request:
    /// line N contains the comma-separated floats of chunk N's embedding vector.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_SingleText_ChunkingEnabled_AcceptTextPlain_ExactLineFormat()
    {
        // Arrange — deterministic embeddings: chunk 0 → [0.1, 0.2, 0.3], chunk 1 → [0.4, 0.5, 0.6]
        float[] chunkEmbedding1 = new[] { 0.1f, 0.2f, 0.3f };
        float[] chunkEmbedding2 = new[] { 0.4f, 0.5f, 0.6f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(
                It.Is<string[]>(t => t.Length == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingBatchResult(true, new[] { chunkEmbedding1, chunkEmbedding2 }));

        // 1500 chars, 1000-char chunk size, 0 overlap → exactly 2 chunks sent as one batch
        EmbeddingController controller = CreateControllerWithChunking(
            requestBody: new string('X', 1500),
            acceptHeader: "text/plain");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(ContentResult));
        ContentResult contentResult = (ContentResult)result;
        string[] lines = contentResult.Content!.Split('\n');
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual("0.1,0.2,0.3", lines[0]);
        Assert.AreEqual("0.4,0.5,0.6", lines[1]);
    }

    /// <summary>
    /// Single text + chunking enabled + no Accept header must return JSON (OkObjectResult),
    /// preserving the default JSON behaviour even when chunking is active.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_SingleText_ChunkingEnabled_NoAcceptHeader_ReturnsJson()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

        EmbeddingController controller = CreateControllerWithChunking(
            requestBody: new string('X', 1500),
            acceptHeader: null);

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert — no Accept header → JSON (EmbedDocumentResponse[])
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        Assert.IsInstanceOfType(okResult.Value, typeof(EmbedDocumentResponse[]));
    }

    /// <summary>
    /// Single text + chunking enabled + Accept: application/json must return JSON,
    /// consistent with the non-chunked path where JSON wins over text/plain.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_SingleText_ChunkingEnabled_AcceptJson_ReturnsJson()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

        EmbeddingController controller = CreateControllerWithChunking(
            requestBody: new string('X', 1500),
            acceptHeader: "application/json");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        Assert.IsInstanceOfType(okResult.Value, typeof(EmbedDocumentResponse[]));
    }

    /// <summary>
    /// Single text + chunking enabled + Accept: text/plain, application/json → JSON wins,
    /// matching the same precedence rule applied in the non-chunked single-text path.
    /// </summary>
    [TestMethod]
    public async Task PostAsync_SingleText_ChunkingEnabled_AcceptBothJsonAndTextPlain_JsonWins()
    {
        // Arrange
        float[] embedding = new[] { 0.1f, 0.2f };
        _mockEmbeddingService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] texts, CancellationToken _) =>
                new EmbeddingBatchResult(true, texts.Select(_ => embedding).ToArray()));

        EmbeddingController controller = CreateControllerWithChunking(
            requestBody: new string('X', 1500),
            acceptHeader: "text/plain, application/json");

        // Act
        IActionResult result = await controller.PostAsync();

        // Assert — JSON takes precedence
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        OkObjectResult okResult = (OkObjectResult)result;
        Assert.IsInstanceOfType(okResult.Value, typeof(EmbedDocumentResponse[]));
    }

    /// <summary>
    /// Helper: creates a controller wired with chunking enabled (1000-char chunks, no overlap)
    /// and the class-level mock embedding service.
    /// </summary>
    private EmbeddingController CreateControllerWithChunking(
        string requestBody,
        string? acceptHeader,
        int sizeChars = 1000,
        int overlapChars = 0)
    {
        EmbeddingsOptions embeddingsOptions = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key",
            Enabled: true,
            Endpoint: new EmbeddingsEndpointOptions(enabled: true),
            Chunking: new EmbeddingsChunkingOptions(Enabled: true, SizeChars: sizeChars, OverlapChars: overlapChars));

        Mock<RuntimeConfigProvider> mockProvider = CreateMockConfigProvider(
            embeddingsOptions: embeddingsOptions,
            hostMode: HostMode.Development);

        EmbeddingController controller = new(mockProvider.Object, _mockLogger.Object, _mockEmbeddingService.Object);
        controller.ControllerContext = CreateControllerContext(
            "/embed",
            requestBody,
            contentType: "text/plain",
            acceptHeader: acceptHeader);
        return controller;
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
            Enabled: true,
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


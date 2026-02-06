// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Controllers;

/// <summary>
/// Controller to serve embedding requests at the configured endpoint path (default: /embed).
/// Accepts plain text input and returns embedding vector as plain text (comma-separated floats).
/// </summary>
[ApiController]
public class EmbeddingController : ControllerBase
{
    private readonly IEmbeddingService? _embeddingService;
    private readonly RuntimeConfigProvider _runtimeConfigProvider;
    private readonly ILogger<EmbeddingController> _logger;

    /// <summary>
    /// Constructor.
    /// </summary>
    public EmbeddingController(
        RuntimeConfigProvider runtimeConfigProvider,
        ILogger<EmbeddingController> logger,
        IEmbeddingService? embeddingService = null)
    {
        _runtimeConfigProvider = runtimeConfigProvider;
        _logger = logger;
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// POST endpoint for generating embeddings.
    /// Accepts plain text body and returns embedding vector as comma-separated floats.
    /// </summary>
    /// <param name="route">The route path.</param>
    /// <returns>Plain text embedding vector or error response.</returns>
    [HttpPost]
    [Route("{*route}")]
    [Consumes("text/plain", "application/json")]
    [Produces("text/plain")]
    public async Task<IActionResult> PostAsync(string? route)
    {
        // Get embeddings configuration
        EmbeddingsOptions? embeddingsOptions = _runtimeConfigProvider.GetConfig()?.Runtime?.Embeddings;
        EmbeddingsEndpointOptions? endpointOptions = embeddingsOptions?.Endpoint;

        // Check if embeddings and endpoint are enabled
        if (embeddingsOptions is null || !embeddingsOptions.Enabled)
        {
            return NotFound();
        }

        if (endpointOptions is null || !endpointOptions.Enabled)
        {
            return NotFound();
        }

        // Check if the route matches the configured endpoint path
        string expectedPath = endpointOptions.EffectivePath.TrimStart('/');
        if (!string.Equals(route, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        // Check if embedding service is available
        if (_embeddingService is null || !_embeddingService.IsEnabled)
        {
            _logger.LogWarning("Embedding endpoint called but embedding service is not available or disabled.");
            return StatusCode((int)HttpStatusCode.ServiceUnavailable, "Embedding service is not available.");
        }

        // Check authorization
        bool isDevelopmentMode = _runtimeConfigProvider.GetConfig()?.Runtime?.Host?.Mode == HostMode.Development;
        string clientRole = GetClientRole();

        if (!endpointOptions.IsRoleAllowed(clientRole, isDevelopmentMode))
        {
            _logger.LogWarning("Embedding endpoint access denied for role: {Role}", clientRole);
            return StatusCode((int)HttpStatusCode.Forbidden, "Access denied. Role not authorized.");
        }

        // Read request body as plain text
        string text;
        try
        {
            using StreamReader reader = new(Request.Body);
            text = await reader.ReadToEndAsync();

            // Handle JSON-wrapped string
            if (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    text = JsonSerializer.Deserialize<string>(text) ?? text;
                }
                catch (JsonException)
                {
                    // Not valid JSON string, use as-is
                    _logger.LogDebug("Request body is not a valid JSON string, using as plain text.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read request body for embedding.");
            return BadRequest("Failed to read request body.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Request body cannot be empty.");
        }

        // Generate embedding
        EmbeddingResult result = await _embeddingService.TryEmbedAsync(text);

        if (!result.Success)
        {
            _logger.LogError("Embedding request failed: {Error}", result.ErrorMessage);
            return StatusCode((int)HttpStatusCode.InternalServerError, result.ErrorMessage ?? "Failed to generate embedding.");
        }

        if (result.Embedding is null || result.Embedding.Length == 0)
        {
            _logger.LogError("Embedding request returned empty result.");
            return StatusCode((int)HttpStatusCode.InternalServerError, "Failed to generate embedding.");
        }

        // Return embedding as comma-separated float values (plain text)
        string embeddingText = string.Join(",", result.Embedding);
        return Content(embeddingText, MediaTypeNames.Text.Plain);
    }

    /// <summary>
    /// Gets the client role from request headers.
    /// </summary>
    private string GetClientRole()
    {
        StringValues roleHeader = Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER];
        string? firstRole = roleHeader.Count == 1 ? roleHeader[0] : null;
        
        if (!string.IsNullOrEmpty(firstRole))
        {
            return firstRole.ToLowerInvariant();
        }

        return EmbeddingsEndpointOptions.ANONYMOUS_ROLE;
    }
}

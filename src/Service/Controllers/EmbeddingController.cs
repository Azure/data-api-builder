// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Controllers;

/// <summary>
/// Controller to serve embedding requests at the fixed endpoint path: /embed.
/// Accepts plain text or JSON input and returns embedding vector as JSON by default,
/// or as plain text (comma-separated floats) when the client sends Accept: text/plain.
/// Uses a dedicated "embed" route to avoid conflicts with other API routes.
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
    /// Accepts JSON string or array of documents with key/text pairs.
    /// Supports query parameters to override chunking settings.
    /// Default response is JSON: { "embedding": [...], "dimensions": N } for single text,
    /// or [{ "key": "...", "data": [[...], [...]] }] for document arrays.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>Embedding vector(s) as JSON, or an error response.</returns>
    [HttpPost]
    [Route("embed")]
    [Consumes("application/json")]
    [Produces("application/json", "text/plain")]
    public async Task<IActionResult> PostAsync(CancellationToken cancellationToken = default)
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

        // Parse query parameters for chunking options
        EmbeddingsChunkingOptions? queryChunkingOptions = ParseChunkingOptionsFromQuery();

        // Read request body
        string requestBody;
        try
        {
            using StreamReader reader = new(Request.Body);
            requestBody = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read request body for embedding.");
            return BadRequest("Failed to read request body.");
        }

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return BadRequest("Request body cannot be empty.");
        }

        // Try to parse as document array first
        try
        {
            EmbedDocumentRequest[]? documents = JsonSerializer.Deserialize<EmbedDocumentRequest[]>(requestBody);

            if (documents is not null && documents.Length > 0)
            {
                // Handle as document array
                return await ProcessDocumentArrayAsync(documents, embeddingsOptions, queryChunkingOptions, cancellationToken);
            }
            else if (documents is not null && documents.Length == 0)
            {
                // Empty document array
                return BadRequest("Document array cannot be empty.");
            }
        }
        catch (JsonException jsonEx)
        {
            // Not a document array, try as single text
            _logger.LogDebug(jsonEx, "Request body is not a document array, trying as single text.");
        }

        // Try to parse as single JSON string
        try
        {
            string? jsonString = JsonSerializer.Deserialize<string>(requestBody);
            if (jsonString is not null)
            {
                // Handle as single text. Apply chunking if enabled.
                return await ProcessSingleTextAsync(jsonString, embeddingsOptions, queryChunkingOptions, cancellationToken);
            }
            else
            {
                // null value is not valid
                return BadRequest("Invalid JSON: null value is not accepted.");
            }
        }
        catch (JsonException ex)
        {
            // Not a valid JSON string either - check if it's an object (invalid format)
            try
            {
                using JsonDocument doc = JsonDocument.Parse(requestBody);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // It's a JSON object but not a valid format
                    return BadRequest("Invalid JSON format. Expected a text string or document array.");
                }
            }
            catch (JsonException)
            {
                // Not valid JSON at all
                _logger.LogError(ex, "Invalid JSON in request body.");
                return BadRequest("Invalid JSON format. Request body must be valid JSON.");
            }

            // Valid JSON but unexpected type
            return BadRequest("Invalid JSON format. Expected a text string or document array.");
        }
    }

    /// <summary>
    /// Processes a single text request. When chunking is enabled, the request is
    /// routed through the document-array path so the response can represent
    /// multiple chunks. When chunking is not enabled, the legacy single-vector
    /// response is preserved for backward compatibility.
    /// </summary>
    private async Task<IActionResult> ProcessSingleTextAsync(
        string text,
        EmbeddingsOptions embeddingsOptions,
        EmbeddingsChunkingOptions? queryChunkingOptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Request body cannot be empty.");
        }

        // Determine effective chunking options
        EmbeddingsChunkingOptions? effectiveChunkingOptions = queryChunkingOptions ?? embeddingsOptions.Chunking;

        // If chunking is enabled, use document array processing for consistent response format
        if (effectiveChunkingOptions is not null && effectiveChunkingOptions.Enabled)
        {
            EmbedDocumentRequest[] documents =
            [
                new EmbedDocumentRequest
                {
                    Key = "input",
                    Text = text
                }
            ];

            return await ProcessDocumentArrayAsync(documents, embeddingsOptions, effectiveChunkingOptions, cancellationToken);
        }

        // No chunking - preserve legacy single-embedding response
        EmbeddingResult result = await _embeddingService!.TryEmbedAsync(text, cancellationToken);

        if (!result.Success)
        {
            string errorMessage = result.ErrorMessage ?? "Failed to generate embedding.";
            _logger.LogError("Embedding request failed: {Error}", errorMessage);
            return StatusCode((int)HttpStatusCode.InternalServerError, errorMessage);
        }

        if (result.Embedding is null || result.Embedding.Length == 0)
        {
            _logger.LogError("Embedding request returned empty result.");
            return StatusCode((int)HttpStatusCode.InternalServerError, "Failed to generate embedding.");
        }

        // Return embedding as plain text (comma-separated floats) when explicitly requested via Accept header.
        if (ClientAcceptsTextPlain())
        {
            string embeddingText = string.Join(",", result.Embedding.Select(f => f.ToString("G", CultureInfo.InvariantCulture)));
            return Content(embeddingText, MediaTypeNames.Text.Plain);
        }

        // Default: return structured JSON response.
        return Ok(new EmbeddingResponse(result.Embedding));
    }

    /// <summary>
    /// Processes a document array request and returns embeddings for each document.
    /// </summary>
    private async Task<IActionResult> ProcessDocumentArrayAsync(
        EmbedDocumentRequest[] documents,
        EmbeddingsOptions embeddingsOptions,
        EmbeddingsChunkingOptions? queryChunkingOptions,
        CancellationToken cancellationToken)
    {
        List<EmbedDocumentResponse> responses = new();

        foreach (EmbedDocumentRequest doc in documents)
        {
            if (string.IsNullOrEmpty(doc.Key))
            {
                return BadRequest("Each document must have a non-empty key.");
            }

            if (string.IsNullOrEmpty(doc.Text))
            {
                return BadRequest($"Document with key '{doc.Key}' has empty text.");
            }

            try
            {
                // Use query params if provided, otherwise fall back to config
                EmbeddingsChunkingOptions? effectiveChunking = queryChunkingOptions ?? embeddingsOptions.Chunking;

                // Chunk the text if chunking is enabled
                string[] chunks = ChunkText(doc.Text, effectiveChunking);

                // Embed all chunks using batch API for better performance
                EmbeddingBatchResult batchResult = await _embeddingService!.TryEmbedBatchAsync(chunks, cancellationToken);

                if (!batchResult.Success || batchResult.Embeddings is null || batchResult.Embeddings.Length == 0)
                {
                    string errorMessage = batchResult.ErrorMessage ?? "Unknown error";
                    _logger.LogError("Failed to embed chunks for document key '{Key}': {Error}", doc.Key, errorMessage);
                    return StatusCode(
                        (int)HttpStatusCode.InternalServerError,
                        $"Failed to generate embeddings for document '{doc.Key}': {errorMessage}");
                }

                responses.Add(new EmbedDocumentResponse(doc.Key, batchResult.Embeddings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document with key '{Key}'", doc.Key);
                return StatusCode(
                    (int)HttpStatusCode.InternalServerError,
                    $"Error processing document '{doc.Key}': {ex.Message}");
            }
        }

        return Ok(responses.ToArray());
    }

    /// <summary>
    /// Parses query parameters and creates EmbeddingsChunkingOptions.
    /// Returns null if no query parameters are provided (use config defaults).
    /// </summary>
    private EmbeddingsChunkingOptions? ParseChunkingOptionsFromQuery()
    {
        bool? enabled = null;
        int? sizeChars = null;
        int? overlapChars = null;

        if (Request.Query.TryGetValue("$chunking.enabled", out StringValues enabledValue))
        {
            if (bool.TryParse(enabledValue, out bool parsedEnabled))
            {
                enabled = parsedEnabled;
            }
        }

        if (Request.Query.TryGetValue("$chunking.size-chars", out StringValues sizeValue))
        {
            if (int.TryParse(sizeValue, out int size) && size > 0)
            {
                sizeChars = size;
            }
        }

        if (Request.Query.TryGetValue("$chunking.overlap-chars", out StringValues overlapValue))
        {
            if (int.TryParse(overlapValue, out int overlap) && overlap >= 0)
            {
                overlapChars = overlap;
            }
        }

        // If no query parameters provided, return null to use config defaults
        if (!enabled.HasValue && !sizeChars.HasValue && !overlapChars.HasValue)
        {
            return null;
        }

        // Create new options with query parameters (using defaults for unspecified values)
        return new EmbeddingsChunkingOptions(enabled, sizeChars, overlapChars);
    }

    /// <summary>
    /// Splits text into chunks if chunking is enabled and text exceeds chunk size.
    /// Uses EffectiveSizeChars to ensure chunk size is always valid.
    /// </summary>
    private static string[] ChunkText(string text, EmbeddingsChunkingOptions? chunkingOptions)
    {
        // If chunking is disabled or options are null, return text as single chunk
        if (chunkingOptions is null || !chunkingOptions.Enabled)
        {
            return new[] { text };
        }

        // Use EffectiveSizeChars to ensure chunk size is at least overlap + 1
        int chunkSize = chunkingOptions.EffectiveSizeChars;
        int overlap = chunkingOptions.OverlapChars;

        // If text fits in one chunk, return as single item
        if (text.Length <= chunkSize)
        {
            return new[] { text };
        }

        List<string> chunks = new();
        int position = 0;

        // Calculate step size to guarantee forward progress
        int step = Math.Max(1, chunkSize - overlap);

        while (position < text.Length)
        {
            int remainingLength = text.Length - position;
            int currentChunkSize = Math.Min(chunkSize, remainingLength);

            chunks.Add(text.Substring(position, currentChunkSize));

            // Always move forward by at least 1 to prevent infinite loops
            position += step;
        }

        return chunks.ToArray();
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

    /// <summary>
    /// Checks whether the client explicitly requests text/plain via the Accept header.
    /// Returns true only when text/plain is present and application/json is not,
    /// so that the default response format remains JSON.
    /// </summary>
    private bool ClientAcceptsTextPlain()
    {
        StringValues acceptHeader = Request.Headers.Accept;
        if (acceptHeader.Count == 0)
        {
            return false;
        }

        string accept = acceptHeader.ToString();
        bool wantsText = accept.Contains(MediaTypeNames.Text.Plain, StringComparison.OrdinalIgnoreCase);
        bool wantsJson = accept.Contains(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase);

        // Only return text/plain when the client explicitly asks for it
        // and does NOT also ask for JSON (in which case JSON wins).
        return wantsText && !wantsJson;
    }
}


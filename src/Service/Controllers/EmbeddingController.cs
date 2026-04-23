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
using Azure.DataApiBuilder.Service.Helpers;
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
    /// Accepts plain text, JSON string, or array of documents with key/text pairs.
    /// Supports query parameters to override chunking settings.
    /// Default response is JSON: { "embedding": [...], "dimensions": N } for single text,
    /// or [{ "key": "...", "data": [[...], [...]] }] for document arrays.
    /// </summary>
    /// <returns>Embedding vector(s) as JSON, or an error response.</returns>
    [HttpPost]
    [Route("embed")]
    [Consumes("text/plain", "application/json")]
    [Produces("application/json", "text/plain")]
    public async Task<IActionResult> PostAsync()
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
            Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            return RestController.ErrorResponse(
                "UnexpectedError",
                "Embedding service is not available.",
                HttpStatusCode.ServiceUnavailable);
        }

        // Check authorization
        bool isDevelopmentMode = _runtimeConfigProvider.GetConfig()?.Runtime?.Host?.Mode == HostMode.Development;
        string clientRole = GetClientRole();

        if (!endpointOptions.IsRoleAllowed(clientRole, isDevelopmentMode))
        {
            _logger.LogWarning("Embedding endpoint access denied for role: {Role}", clientRole);
            Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return RestController.ErrorResponse(
                "AuthorizationCheckFailed",
                "Access denied.",
                HttpStatusCode.Forbidden);
        }

        // Parse query parameters for chunking options
        EmbeddingsChunkingOptions? queryChunkingOptions = ParseChunkingOptionsFromQuery(out string? paramValidationError);
        if (paramValidationError is not null)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return RestController.ErrorResponse("BadRequest", paramValidationError, HttpStatusCode.BadRequest);
        }

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
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return RestController.ErrorResponse("BadRequest", "Failed to read request body.", HttpStatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return RestController.ErrorResponse("BadRequest", "Request body cannot be empty.", HttpStatusCode.BadRequest);
        }

        CancellationToken cancellationToken = HttpContext.RequestAborted;

        // Try to parse as document array first (if JSON content type)
        if (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
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
                    Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return RestController.ErrorResponse("BadRequest", "Document array cannot be empty.", HttpStatusCode.BadRequest);
                }
            }
            catch (JsonException)
            {
                // Not a document array, try as single text
                _logger.LogDebug("Request body is not a document array, trying as single text.");
            }

            // Try to parse as single JSON string
            try
            {
                string? jsonString = JsonSerializer.Deserialize<string>(requestBody);
                if (jsonString is not null)
                {
                    requestBody = jsonString;
                }
                else
                {
                    Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return RestController.ErrorResponse("BadRequest", "JSON request body must be a non-null string or a document array.", HttpStatusCode.BadRequest);
                }
            }
            catch (JsonException)
            {
                // Body is application/json but neither an array nor a string (e.g. {"foo":"bar"})
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return RestController.ErrorResponse("BadRequest", "Request body with content type 'application/json' must be a JSON string or a document array.", HttpStatusCode.BadRequest);
            }
        }

        // Handle as single text, applying chunking when enabled
        return await ProcessSingleTextAsync(requestBody, embeddingsOptions, queryChunkingOptions, cancellationToken);
    }

    /// <summary>
    /// Processes a document array request and returns embeddings for each document.
    /// Uses batch embedding (TryEmbedBatchAsync) per document to reduce round-trips.
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
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return RestController.ErrorResponse("BadRequest", "Each document must have a non-empty key.", HttpStatusCode.BadRequest);
            }

            if (string.IsNullOrEmpty(doc.Text))
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return RestController.ErrorResponse("BadRequest", "Document with key has empty text.", HttpStatusCode.BadRequest);
            }

            try
            {
                // Use query params if provided, otherwise fall back to config
                EmbeddingsChunkingOptions? effectiveChunking = queryChunkingOptions ?? embeddingsOptions.Chunking;

                // Chunk the text if chunking is enabled
                string[] chunks = TextChunker.ChunkText(doc.Text, effectiveChunking);

                // Batch-embed all chunks for this document in a single request
                EmbeddingBatchResult batchResult = await _embeddingService!.TryEmbedBatchAsync(chunks, cancellationToken);

                if (!batchResult.Success || batchResult.Embeddings is null)
                {
                    _logger.LogError("Failed to embed document chunks: {Error}", batchResult.ErrorMessage);
                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return RestController.ErrorResponse(
                        "UnexpectedError",
                        "Failed to generate embeddings.",
                        HttpStatusCode.InternalServerError);
                }

                responses.Add(new EmbedDocumentResponse(doc.Key, batchResult.Embeddings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document.");
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return RestController.ErrorResponse(
                    "UnexpectedError",
                    "Failed to generate embeddings.",
                    HttpStatusCode.InternalServerError);
            }
        }

        return Ok(responses.ToArray());
    }

    /// <summary>
    /// Routes a single-text request through chunking when enabled, falling back to the
    /// legacy single-embedding response for backward compatibility when not chunked.
    /// </summary>
    private async Task<IActionResult> ProcessSingleTextAsync(
        string text,
        EmbeddingsOptions embeddingsOptions,
        EmbeddingsChunkingOptions? queryChunkingOptions,
        CancellationToken cancellationToken)
    {
        EmbeddingsChunkingOptions? effectiveChunking = queryChunkingOptions ?? embeddingsOptions.Chunking;

        if (effectiveChunking is not null && effectiveChunking.Enabled)
        {
            // Route through document-array path to produce a multi-chunk response
            EmbedDocumentRequest[] documents =
            [
                new EmbedDocumentRequest { Key = "input", Text = text }
            ];
            IActionResult result = await ProcessDocumentArrayAsync(documents, embeddingsOptions, effectiveChunking, cancellationToken);

            // Apply text/plain format when requested, consistent with the non-chunked path.
            // Each chunk's embedding is output as one line of comma-separated floats.
            if (ClientAcceptsTextPlain() && result is OkObjectResult okResult && okResult.Value is EmbedDocumentResponse[] docResponses)
            {
                IEnumerable<string> lines = docResponses
                    .SelectMany(d => d.Data)
                    .Select(embedding => string.Join(",", embedding.Select(f => f.ToString("G", CultureInfo.InvariantCulture))));
                return Content(string.Join("\n", lines), MediaTypeNames.Text.Plain);
            }

            return result;
        }

        return await ProcessSingleTextAsync(text, cancellationToken);
    }

    /// <summary>
    /// Processes a single text request and returns embedding (backward compatible, no chunking).
    /// </summary>
    private async Task<IActionResult> ProcessSingleTextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return RestController.ErrorResponse("BadRequest", "Request body cannot be empty.", HttpStatusCode.BadRequest);
        }

        // Generate embedding
        EmbeddingResult result = await _embeddingService!.TryEmbedAsync(text, cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Embedding request failed: {Error}", result.ErrorMessage);
            Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return RestController.ErrorResponse(
                "UnexpectedError",
                "Failed to generate embedding.",
                HttpStatusCode.InternalServerError);
        }

        if (result.Embedding is null || result.Embedding.Length == 0)
        {
            _logger.LogError("Embedding request returned empty result.");
            Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return RestController.ErrorResponse(
                "UnexpectedError",
                "Failed to generate embedding.",
                HttpStatusCode.InternalServerError);
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
    /// Parses query parameters and creates EmbeddingsChunkingOptions.
    /// Returns null if no query parameters are provided (use config defaults).
    /// Sets <paramref name="validationError"/> to a non-null message if any provided param is invalid.
    /// </summary>
    private EmbeddingsChunkingOptions? ParseChunkingOptionsFromQuery(out string? validationError)
    {
        validationError = null;
        bool? enabled = null;
        int? sizeChars = null;
        int? overlapChars = null;

        if (Request.Query.TryGetValue("$chunking.enabled", out StringValues enabledValue))
        {
            if (bool.TryParse(enabledValue, out bool parsedEnabled))
            {
                enabled = parsedEnabled;
            }
            else
            {
                validationError = $"Invalid value for '$chunking.enabled': must be 'true' or 'false'.";
                return null;
            }
        }

        if (Request.Query.TryGetValue("$chunking.size-chars", out StringValues sizeValue))
        {
            if (int.TryParse(sizeValue, out int size) && size > 0)
            {
                sizeChars = size;
            }
            else
            {
                validationError = $"Invalid value for '$chunking.size-chars': must be a positive integer.";
                return null;
            }
        }

        if (Request.Query.TryGetValue("$chunking.overlap-chars", out StringValues overlapValue))
        {
            if (int.TryParse(overlapValue, out int overlap) && overlap >= 0)
            {
                overlapChars = overlap;
            }
            else
            {
                validationError = $"Invalid value for '$chunking.overlap-chars': must be a non-negative integer.";
                return null;
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


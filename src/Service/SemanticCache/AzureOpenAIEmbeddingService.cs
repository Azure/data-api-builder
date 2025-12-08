// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.SemanticCache;

/// <summary>
/// Azure OpenAI implementation of the embedding service.
/// </summary>
public class AzureOpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingProviderOptions _options;
    private readonly ILogger<AzureOpenAIEmbeddingService> _logger;
    private readonly HttpClient _httpClient;
    private const string API_VERSION = "2024-02-01";
    private const int MAX_RETRIES = 3;
    private const int INITIAL_RETRY_DELAY_MS = 1000;

    public AzureOpenAIEmbeddingService(
        EmbeddingProviderOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAIEmbeddingService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClientFactory.CreateClient();

        if (string.IsNullOrEmpty(_options.Endpoint))
        {
            throw new ArgumentException("Embedding provider endpoint is required.", nameof(options));
        }

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            throw new ArgumentException("Embedding provider API key is required.", nameof(options));
        }

        if (string.IsNullOrEmpty(_options.Model))
        {
            throw new ArgumentException("Embedding provider model is required.", nameof(options));
        }

        // Configure HTTP client
        _httpClient.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc/>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        }

        int attempt = 0;
        Exception? lastException = null;

        while (attempt < MAX_RETRIES)
        {
            try
            {
                attempt++;
                _logger.LogDebug(
                    "Generating embedding for text of length {TextLength} (attempt {Attempt}/{MaxRetries})",
                    text.Length,
                    attempt,
                    MAX_RETRIES);

                // Build the Azure OpenAI embeddings endpoint URL
                string endpoint = _options.Endpoint!.TrimEnd('/');
                string url = $"{endpoint}/openai/deployments/{_options.Model}/embeddings?api-version={API_VERSION}";

                // Create the request payload
                var requestBody = new EmbeddingRequest { Input = text };

                // Send POST request
                using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                    url,
                    requestBody,
                    cancellationToken);

                // Handle rate limiting with exponential backoff
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt < MAX_RETRIES)
                    {
                        int delayMs = INITIAL_RETRY_DELAY_MS * (int)Math.Pow(2, attempt - 1);
                        _logger.LogWarning(
                            "Rate limited by Azure OpenAI. Retrying after {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                            delayMs,
                            attempt,
                            MAX_RETRIES);
                        await Task.Delay(delayMs, cancellationToken);
                        continue;
                    }
                }

                // Ensure successful response
                response.EnsureSuccessStatusCode();

                // Parse response
                string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                EmbeddingResponse? embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(
                    responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (embeddingResponse?.Data == null || embeddingResponse.Data.Count == 0)
                {
                    throw new InvalidOperationException("Azure OpenAI returned an empty embedding response.");
                }

                float[] embedding = embeddingResponse.Data[0].Embedding;

                _logger.LogInformation(
                    "Successfully generated embedding with {Dimensions} dimensions (tokens used: {TokensUsed})",
                    embedding.Length,
                    embeddingResponse.Usage?.TotalTokens ?? 0);

                return embedding;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "HTTP request failed for embedding generation (attempt {Attempt}/{MaxRetries})",
                    attempt,
                    MAX_RETRIES);

                if (attempt < MAX_RETRIES)
                {
                    int delayMs = INITIAL_RETRY_DELAY_MS * (int)Math.Pow(2, attempt - 1);
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                _logger.LogInformation("Embedding generation was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating embedding for text");
                throw;
            }
        }

        // If all retries failed
        throw new InvalidOperationException(
            $"Failed to generate embedding after {MAX_RETRIES} attempts.",
            lastException);
    }

    // Request/Response DTOs for Azure OpenAI Embeddings API
    private class EmbeddingRequest
    {
        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = new();

        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();

        [JsonPropertyName("index")]
        public int Index { get; set; }
    }

    private class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}

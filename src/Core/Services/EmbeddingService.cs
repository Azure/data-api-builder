// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Services;

/// <summary>
/// Service implementation for text embedding/vectorization.
/// Supports both OpenAI and Azure OpenAI providers.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingsOptions _options;
    private readonly ILogger<EmbeddingService> _logger;

    /// <summary>
    /// JSON serializer options for request/response handling.
    /// </summary>
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new instance of the EmbeddingService.
    /// </summary>
    /// <param name="httpClient">The HTTP client factory for creating HTTP clients.</param>
    /// <param name="options">The embedding configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    public EmbeddingService(
        HttpClient httpClient,
        EmbeddingsOptions options,
        ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ConfigureHttpClient();
    }

    /// <summary>
    /// Configures the HTTP client with timeout and authentication headers.
    /// </summary>
    private void ConfigureHttpClient()
    {
        _httpClient.Timeout = TimeSpan.FromMilliseconds(_options.EffectiveTimeoutMs);

        if (_options.Provider == EmbeddingProviderType.AzureOpenAI)
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        }

        float[][] results = await EmbedBatchAsync(new[] { text }, cancellationToken);
        return results[0];
    }

    /// <inheritdoc/>
    public async Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        if (texts is null || texts.Length == 0)
        {
            throw new ArgumentException("Texts cannot be null or empty.", nameof(texts));
        }

        string requestUrl = BuildRequestUrl();
        object requestBody = BuildRequestBody(texts);

        string requestJson = JsonSerializer.Serialize(requestBody, _jsonSerializerOptions);
        using HttpContent content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending embedding request to {Url} with {Count} text(s)", requestUrl, texts.Length);

        HttpResponseMessage response = await _httpClient.PostAsync(requestUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Embedding request failed with status {StatusCode}: {ErrorContent}",
                response.StatusCode, errorContent);
            throw new HttpRequestException(
                $"Embedding request failed with status code {response.StatusCode}: {errorContent}");
        }

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        EmbeddingResponse? embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(responseJson, _jsonSerializerOptions);

        if (embeddingResponse?.Data is null || embeddingResponse.Data.Count == 0)
        {
            throw new InvalidOperationException("No embedding data received from the provider.");
        }

        // Sort by index to ensure correct order and extract embeddings
        List<EmbeddingData> sortedData = embeddingResponse.Data.OrderBy(d => d.Index).ToList();
        return sortedData.Select(d => d.Embedding).ToArray();
    }

    /// <summary>
    /// Builds the request URL based on the provider type.
    /// </summary>
    private string BuildRequestUrl()
    {
        string endpoint = _options.Endpoint.TrimEnd('/');

        if (_options.Provider == EmbeddingProviderType.AzureOpenAI)
        {
            // Azure OpenAI: {endpoint}/openai/deployments/{deployment}/embeddings?api-version={version}
            string model = _options.EffectiveModel
                ?? throw new InvalidOperationException("Model/deployment name is required for Azure OpenAI.");

            return $"{endpoint}/openai/deployments/{model}/embeddings?api-version={_options.EffectiveApiVersion}";
        }
        else
        {
            // OpenAI: {endpoint}/v1/embeddings
            return $"{endpoint}/v1/embeddings";
        }
    }

    /// <summary>
    /// Builds the request body based on the provider type.
    /// </summary>
    private object BuildRequestBody(string[] texts)
    {
        // Use single string for single text, array for batch
        object input = texts.Length == 1 ? texts[0] : texts;

        if (_options.Provider == EmbeddingProviderType.AzureOpenAI)
        {
            // Azure OpenAI request body
            if (_options.UserProvidedDimensions)
            {
                return new
                {
                    input,
                    dimensions = _options.Dimensions
                };
            }

            return new { input };
        }
        else
        {
            // OpenAI request body - includes model in body
            string model = _options.EffectiveModel ?? EmbeddingsOptions.DEFAULT_OPENAI_MODEL;

            if (_options.UserProvidedDimensions)
            {
                return new
                {
                    model,
                    input,
                    dimensions = _options.Dimensions
                };
            }

            return new
            {
                model,
                input
            };
        }
    }

    /// <summary>
    /// Response model for embedding API responses.
    /// </summary>
    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public EmbeddingUsage? Usage { get; set; }
    }

    /// <summary>
    /// Individual embedding data in the response.
    /// </summary>
    private sealed class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }

    /// <summary>
    /// Token usage information in the response.
    /// </summary>
    private sealed class EmbeddingUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}

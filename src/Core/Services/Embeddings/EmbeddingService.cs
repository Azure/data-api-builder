// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Core.Services.Embeddings;

/// <summary>
/// Service implementation for text embedding/vectorization.
/// Supports both OpenAI and Azure OpenAI providers.
/// Includes L1 memory cache using FusionCache to prevent duplicate embedding API calls.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingsOptions _options;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly IFusionCache _cache;
    private readonly string _providerName;

    // Constants
    private const char KEY_DELIMITER = ':';
    private const string CACHE_KEY_PREFIX = "embedding";

    /// <summary>
    /// Default cache TTL in hours. Set high since embeddings are deterministic and don't get outdated.
    /// </summary>
    private const int DEFAULT_CACHE_TTL_HOURS = 24;

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
    /// <param name="httpClient">The HTTP client for making API requests.</param>
    /// <param name="options">The embedding configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cache">The FusionCache instance for L1 memory caching.</param>
    public EmbeddingService(
        HttpClient httpClient,
        EmbeddingsOptions options,
        ILogger<EmbeddingService> logger,
        IFusionCache cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        // Cache provider name for telemetry to avoid repeated string allocations
        _providerName = _options.Provider.ToString().ToLowerInvariant();

        // Validate required options
        if (string.IsNullOrEmpty(_options.BaseUrl))
        {
            throw new ArgumentException("BaseUrl is required in EmbeddingsOptions.", nameof(options));
        }

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            throw new ArgumentException("ApiKey is required in EmbeddingsOptions.", nameof(options));
        }

        // Azure OpenAI requires model/deployment name
        if (_options.Provider == EmbeddingProviderType.AzureOpenAI && string.IsNullOrEmpty(_options.EffectiveModel))
        {
            throw new InvalidOperationException("Model/deployment name is required for Azure OpenAI provider.");
        }

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
    public bool IsEnabled => _options.Enabled;

    /// <inheritdoc/>
    public async Task<EmbeddingResult> TryEmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Embedding service is disabled, skipping embed request");
            return new EmbeddingResult(false, null, "Embedding service is disabled.");
        }

        if (string.IsNullOrEmpty(text))
        {
            _logger.LogWarning("TryEmbedAsync called with null or empty text");
            return new EmbeddingResult(false, null, "Text cannot be null or empty.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Activity? activity = EmbeddingTelemetryHelper.StartEmbeddingActivity("TryEmbedAsync");
        activity?.SetEmbeddingActivityTags(_providerName, _options.EffectiveModel, textCount: 1);

        try
        {
            EmbeddingTelemetryHelper.TrackEmbeddingRequest(_providerName, textCount: 1);

            float[] embedding = await EmbedAsync(text, cancellationToken);

            stopwatch.Stop();
            activity?.SetEmbeddingActivitySuccess(stopwatch.Elapsed.TotalMilliseconds, embedding.Length);
            EmbeddingTelemetryHelper.TrackTotalDuration(_providerName, stopwatch.Elapsed, fromCache: false);
            EmbeddingTelemetryHelper.TrackDimensions(_providerName, embedding.Length);

            return new EmbeddingResult(true, embedding);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to generate embedding for text");
            activity?.SetEmbeddingActivityError(ex);
            EmbeddingTelemetryHelper.TrackError(_providerName, ex.GetType().Name);

            return new EmbeddingResult(false, null, ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<EmbeddingBatchResult> TryEmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Embedding service is disabled, skipping batch embed request");
            return new EmbeddingBatchResult(false, null, "Embedding service is disabled.");
        }

        if (texts is null || texts.Length == 0)
        {
            _logger.LogWarning("TryEmbedBatchAsync called with null or empty texts array");
            return new EmbeddingBatchResult(false, null, "Texts array cannot be null or empty.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Activity? activity = EmbeddingTelemetryHelper.StartEmbeddingActivity("TryEmbedBatchAsync");
        activity?.SetEmbeddingActivityTags(_providerName, _options.EffectiveModel, texts.Length);

        try
        {
            EmbeddingTelemetryHelper.TrackEmbeddingRequest(_providerName, texts.Length);

            float[][] embeddings = await EmbedBatchAsync(texts, cancellationToken);

            stopwatch.Stop();
            int dimensions = embeddings.Length > 0 ? embeddings[0].Length : 0;
            activity?.SetEmbeddingActivitySuccess(stopwatch.Elapsed.TotalMilliseconds, dimensions);
            EmbeddingTelemetryHelper.TrackTotalDuration(_providerName, stopwatch.Elapsed, fromCache: false);
            if (dimensions > 0)
            {
                EmbeddingTelemetryHelper.TrackDimensions(_providerName, dimensions);
            }

            return new EmbeddingBatchResult(true, embeddings);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to generate embeddings for batch of {Count} texts", texts.Length);
            activity?.SetEmbeddingActivityError(ex);
            EmbeddingTelemetryHelper.TrackError(_providerName, ex.GetType().Name);

            return new EmbeddingBatchResult(false, null, ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Embedding service is disabled.");
        }

        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        }

        string cacheKey = CreateCacheKey(text);

        float[]? embedding = await _cache.GetOrSetAsync<float[]>(
            key: cacheKey,
            async (FusionCacheFactoryExecutionContext<float[]> ctx, CancellationToken ct) =>
            {
                _logger.LogDebug("Embedding cache miss, calling API for text hash {TextHash}", cacheKey);

                float[][] results = await EmbedFromApiAsync(new[] { text }, ct);
                float[] result = results[0];

                // L1 only - skip distributed cache
                ctx.Options.SetSkipDistributedCache(true, true);
                ctx.Options.SetDuration(TimeSpan.FromHours(DEFAULT_CACHE_TTL_HOURS));

                return result;
            },
            token: cancellationToken);

        if (embedding is null)
        {
            throw new InvalidOperationException("Failed to get embedding from cache or API.");
        }

        return embedding;
    }

    /// <inheritdoc/>
    public async Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Embedding service is disabled.");
        }
        if (texts is null || texts.Length == 0)
        {
            throw new ArgumentException("Texts cannot be null or empty.", nameof(texts));
        }

        // For batch, check cache for each text individually
        string[] cacheKeys = texts.Select(CreateCacheKey).ToArray();
        float[]?[] results = new float[texts.Length][];
        List<int> uncachedIndices = new();
        int cacheHits = 0;

        // Check cache for each text
        for (int i = 0; i < texts.Length; i++)
        {
            MaybeValue<float[]> cached = _cache.TryGet<float[]>(key: cacheKeys[i]);

            if (cached.HasValue)
            {
                _logger.LogDebug("Embedding cache hit for text hash {TextHash}", cacheKeys[i]);
                results[i] = cached.Value;
                cacheHits++;
                EmbeddingTelemetryHelper.TrackCacheHit(_providerName);
            }
            else
            {
                uncachedIndices.Add(i);
                EmbeddingTelemetryHelper.TrackCacheMiss(_providerName);
            }
        }

        // If all texts were cached, return immediately
        if (uncachedIndices.Count == 0)
        {
            return results!;
        }

        _logger.LogDebug("Embedding cache miss for {Count} text(s), calling API", uncachedIndices.Count);

        // Call API for uncached texts only
        string[] uncachedTexts = uncachedIndices.Select(i => texts[i]).ToArray();

        Stopwatch apiStopwatch = Stopwatch.StartNew();
        float[][] apiResults = await EmbedFromApiAsync(uncachedTexts, cancellationToken);
        apiStopwatch.Stop();

        // Track API call telemetry
        EmbeddingTelemetryHelper.TrackApiCall(_providerName, uncachedTexts.Length);
        EmbeddingTelemetryHelper.TrackApiDuration(_providerName, apiStopwatch.Elapsed, uncachedTexts.Length);

        // Cache new results and merge with cached results
        for (int i = 0; i < uncachedIndices.Count; i++)
        {
            int originalIndex = uncachedIndices[i];
            results[originalIndex] = apiResults[i];

            // Store in L1 cache only
            _cache.Set(
                key: cacheKeys[originalIndex],
                value: apiResults[i],
                options =>
                {
                    options.SetSkipDistributedCache(true, true);
                    options.SetDuration(TimeSpan.FromHours(DEFAULT_CACHE_TTL_HOURS));
                });
        }

        return results!;
    }

    /// <summary>
    /// Creates a cache key from the text using SHA256 hash.
    /// Format: embedding:{provider}:{model}:{SHA256_hash}
    /// Includes provider and model to prevent cross-configuration collisions.
    /// Uses hash to keep cache keys small and deterministic.
    /// </summary>
    /// <param name="text">The text to create a cache key for.</param>
    /// <returns>Cache key string.</returns>
    private string CreateCacheKey(string text)
    {
        // Include provider and model in hash to avoid cross-provider/model collisions
        string keyInput = $"{_options.Provider}:{_options.EffectiveModel}:{text}";
        byte[] textBytes = Encoding.UTF8.GetBytes(keyInput);
        byte[] hashBytes = SHA256.HashData(textBytes);
        string hashHex = Convert.ToHexString(hashBytes);

        StringBuilder cacheKeyBuilder = new();
        cacheKeyBuilder.Append(CACHE_KEY_PREFIX);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(hashHex);

        return cacheKeyBuilder.ToString();
    }

    /// <summary>
    /// Calls the embedding API to get embeddings for the provided texts.
    /// </summary>
    private async Task<float[][]> EmbedFromApiAsync(string[] texts, CancellationToken cancellationToken)
    {
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
        string baseUrl = _options.BaseUrl.TrimEnd('/');

        if (_options.Provider == EmbeddingProviderType.AzureOpenAI)
        {
            // Azure OpenAI: {baseUrl}/openai/deployments/{deployment}/embeddings?api-version={version}
            string model = _options.EffectiveModel
                ?? throw new InvalidOperationException("Model/deployment name is required for Azure OpenAI.");

            return $"{baseUrl}/openai/deployments/{model}/embeddings?api-version={_options.EffectiveApiVersion}";
        }
        else
        {
            // OpenAI: {baseUrl}/v1/embeddings
            return $"{baseUrl}/v1/embeddings";
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

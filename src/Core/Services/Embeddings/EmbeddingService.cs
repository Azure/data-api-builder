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
/// Caches embeddings using FusionCache L1 memory cache.
/// L2/distributed cache is optional globally and is used by this service when configured.
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
    /// Maximum number of text chunks accepted in one batch embedding request.
    /// This protects the system from accidentally submitting extremely large arrays.
    /// </summary>
    public const int MAX_BATCH_TEXT_COUNT = 2048;

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
    /// <param name="cache">The FusionCache instance used for caching embedding vectors.</param>
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
        EmbeddingResult? validationResult = ValidateTryEmbedRequest(text);
        if (validationResult != null)
        {
            return validationResult;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Activity? activity = EmbeddingTelemetryHelper.StartEmbeddingActivity("TryEmbedAsync");
        activity?.SetEmbeddingActivityTags(_providerName, _options.EffectiveModel, textCount: 1);

        try
        {
            EmbeddingTelemetryHelper.TrackEmbeddingRequest(_providerName, textCount: 1);

            (float[] embedding, bool fromCache) = await EmbedWithCacheInfoAsync(text, cancellationToken);

            stopwatch.Stop();
            activity?.SetEmbeddingActivitySuccess(stopwatch.Elapsed.TotalMilliseconds, embedding.Length);
            EmbeddingTelemetryHelper.TrackTotalDuration(_providerName, stopwatch.Elapsed, fromCache: fromCache);
            EmbeddingTelemetryHelper.TrackDimensions(_providerName, embedding.Length);

            if (fromCache)
            {
                EmbeddingTelemetryHelper.TrackCacheHit(_providerName);
            }
            else
            {
                EmbeddingTelemetryHelper.TrackCacheMiss(_providerName);
            }

            return new EmbeddingResult(true, embedding);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to generate embedding for text");
            activity?.SetEmbeddingActivityError(ex);
            EmbeddingTelemetryHelper.TrackError(_providerName, ex.GetType().Name);

            return new EmbeddingResult(false, null, "Failed to generate embedding.");
        }
    }

    /// <inheritdoc/>
    public async Task<EmbeddingBatchResult> TryEmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        EmbeddingBatchResult? validationResult = ValidateTryEmbedBatchRequest(texts);
        if (validationResult != null)
        {
            return validationResult;
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

            return new EmbeddingBatchResult(false, null, "Failed to generate embeddings.");
        }
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ValidateEmbedRequest(text);

        (float[] embedding, _) = await EmbedWithCacheInfoAsync(text, cancellationToken);
        return embedding;
    }

    /// <summary>
    /// Validates the single text embedding request parameters.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when the embedding service is disabled.</exception>
    /// <exception cref="ArgumentException">Thrown when text is invalid.</exception>
    private void ValidateEmbedRequest(string text)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Embedding service is disabled.");
        }

        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        }
    }

    /// <summary>
    /// Validates the single text embedding request for Try methods.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    /// <returns>An EmbeddingResult with error details if validation fails, null if validation passes.</returns>
    private EmbeddingResult? ValidateTryEmbedRequest(string text)
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

        return null;
    }

    /// <summary>
    /// Validates the batch embedding request parameters.
    /// </summary>
    /// <param name="texts">The array of texts to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when the embedding service is disabled.</exception>
    /// <exception cref="ArgumentException">Thrown when texts are invalid.</exception>
    private void ValidateEmbedBatchRequest(string[] texts)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Embedding service is disabled.");
        }

        if (texts is null || texts.Length == 0)
        {
            throw new ArgumentException("Texts cannot be null or empty.", nameof(texts));
        }

        if (texts.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("Texts array must not contain null or empty entries.", nameof(texts));
        }

        if (texts.Length > MAX_BATCH_TEXT_COUNT)
        {
            throw new ArgumentException(
                $"Texts array exceeds max supported batch size of {MAX_BATCH_TEXT_COUNT}.",
                nameof(texts));
        }
    }

    /// <summary>
    /// Validates the batch embedding request for Try methods.
    /// </summary>
    /// <param name="texts">The array of texts to validate.</param>
    /// <returns>An EmbeddingBatchResult with error details if validation fails, null if validation passes.</returns>
    private EmbeddingBatchResult? ValidateTryEmbedBatchRequest(string[] texts)
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

        if (texts.Any(string.IsNullOrEmpty))
        {
            _logger.LogWarning("TryEmbedBatchAsync called with one or more null or empty texts");
            return new EmbeddingBatchResult(false, null, "Texts array must not contain null or empty entries.");
        }

        if (texts.Length > MAX_BATCH_TEXT_COUNT)
        {
            _logger.LogWarning(
                "TryEmbedBatchAsync called with {Count} texts, which exceeds max supported batch size {MaxBatchSize}",
                texts.Length,
                MAX_BATCH_TEXT_COUNT);
            return new EmbeddingBatchResult(
                false,
                null,
                $"Texts array exceeds max supported batch size of {MAX_BATCH_TEXT_COUNT}.");
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default)
    {
        ValidateEmbedBatchRequest(texts);

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
            _logger.LogDebug("All {Count} texts were cache hits, returning cached embeddings", texts.Length);
            return results!;
        }

        _logger.LogDebug("Embedding cache miss for {Count} text(s), calling API", uncachedIndices.Count);

        // Deduplicate uncached texts to minimize API calls
        // Group by text content to find duplicates
        Dictionary<string, List<int>> textToIndices = new();
        foreach (int index in uncachedIndices)
        {
            string text = texts[index];
            if (!textToIndices.ContainsKey(text))
            {
                textToIndices[text] = new List<int>();
            }
            textToIndices[text].Add(index);
        }

        // Get unique uncached texts only
        string[] uniqueUncachedTexts = textToIndices.Keys.ToArray();
        int duplicatesAvoided = uncachedIndices.Count - uniqueUncachedTexts.Length;

        if (duplicatesAvoided > 0)
        {
            _logger.LogDebug(
                "Detected {DuplicateCount} duplicate text(s) in batch, sending {UniqueCount} unique text(s) to API instead of {TotalCount}",
                duplicatesAvoided,
                uniqueUncachedTexts.Length,
                uncachedIndices.Count);
        }

        // Call API for unique uncached texts only
        Stopwatch apiStopwatch = Stopwatch.StartNew();
        float[][] apiResults = await EmbedFromApiAsync(uniqueUncachedTexts, cancellationToken);
        apiStopwatch.Stop();

        // Track API call telemetry (based on actual API calls made)
        EmbeddingTelemetryHelper.TrackApiCall(_providerName, uniqueUncachedTexts.Length);
        EmbeddingTelemetryHelper.TrackApiDuration(_providerName, apiStopwatch.Elapsed, uniqueUncachedTexts.Length);

        // Build a mapping from unique text to its embedding
        Dictionary<string, float[]> textToEmbedding = new();
        for (int i = 0; i < uniqueUncachedTexts.Length; i++)
        {
            textToEmbedding[uniqueUncachedTexts[i]] = apiResults[i];
        }

        // Cache new results and populate results array for all indices (including duplicates)
        foreach (KeyValuePair<string, List<int>> kvp in textToIndices)
        {
            string text = kvp.Key;
            float[] embedding = textToEmbedding[text];
            string cacheKey = cacheKeys[kvp.Value[0]]; // All duplicate texts have the same cache key

            // Cache the embedding once
            _cache.Set(
                key: cacheKey,
                value: embedding,
                options =>
                {
                    options.SetDuration(TimeSpan.FromHours(DEFAULT_CACHE_TTL_HOURS));
                });

            // Populate results for all indices that had this text
            foreach (int originalIndex in kvp.Value)
            {
                results[originalIndex] = embedding;
            }
        }

        return results!;
    }

    /// <summary>
    /// Internal helper that embeds text using cache and returns whether the result came from cache.
    /// </summary>
    private async Task<(float[] Embedding, bool FromCache)> EmbedWithCacheInfoAsync(string text, CancellationToken cancellationToken)
    {
        string cacheKey = CreateCacheKey(text);
        bool fromCache = true;

        float[]? embedding = await _cache.GetOrSetAsync<float[]>(
            key: cacheKey,
            async (FusionCacheFactoryExecutionContext<float[]> ctx, CancellationToken ct) =>
            {
                fromCache = false;
                _logger.LogDebug("Embedding cache miss, calling API for text hash {TextHash}", cacheKey);

                float[][] results = await EmbedFromApiAsync(new[] { text }, ct);
                float[] result = results[0];

                // Validate the embedding result is not empty
                if (result.Length == 0)
                {
                    throw new InvalidOperationException("API returned empty embedding array.");
                }

                // Respect configured cache layers (L1 and optional L2).
                ctx.Options.SetDuration(TimeSpan.FromHours(DEFAULT_CACHE_TTL_HOURS));

                return result;
            },
            token: cancellationToken);

        if (embedding is null || embedding.Length == 0)
        {
            throw new InvalidOperationException("Failed to get embedding from cache or API.");
        }

        return (embedding, fromCache);
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
        string model = _options.EffectiveModel ?? "unknown";

        StringBuilder cacheKeyBuilder = new();
        cacheKeyBuilder.Append(CACHE_KEY_PREFIX);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(_providerName);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(model);
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

        using HttpResponseMessage response = await _httpClient.PostAsync(requestUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Embedding request failed with status {StatusCode}: {ErrorContent}",
                response.StatusCode, errorContent);
            throw new HttpRequestException(
                $"Embedding request failed with status code {(int)response.StatusCode}.");
        }

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        EmbeddingResponse? embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(responseJson, _jsonSerializerOptions);

        if (embeddingResponse?.Data is null || embeddingResponse.Data.Count == 0)
        {
            throw new InvalidOperationException("No embedding data received from the provider.");
        }

        List<EmbeddingData> data = embeddingResponse.Data;
        int expectedCount = texts.Length;

        // Validate that we received exactly one embedding per input text.
        if (data.Count != expectedCount)
        {
            _logger.LogError(
                "Embedding provider returned {ActualCount} embeddings for {ExpectedCount} input text(s).",
                data.Count,
                expectedCount);
            throw new InvalidOperationException(
                $"Embedding provider returned {data.Count} embeddings for {expectedCount} input text(s).");
        }

        // Validate indices are within range and unique.
        int minIndex = data.Min(d => d.Index);
        int maxIndex = data.Max(d => d.Index);
        if (minIndex < 0 || maxIndex >= expectedCount)
        {
            _logger.LogError(
                "Embedding provider returned out-of-range indices. MinIndex: {MinIndex}, MaxIndex: {MaxIndex}, ExpectedCount: {ExpectedCount}.",
                minIndex,
                maxIndex,
                expectedCount);
            throw new InvalidOperationException(
                $"Embedding provider returned out-of-range indices. MinIndex: {minIndex}, MaxIndex: {maxIndex}, ExpectedCount: {expectedCount}.");
        }

        int distinctIndexCount = data.Select(d => d.Index).Distinct().Count();
        if (distinctIndexCount != expectedCount)
        {
            _logger.LogError(
                "Embedding provider returned duplicate or missing indices. DistinctIndexCount: {DistinctIndexCount}, ExpectedCount: {ExpectedCount}.",
                distinctIndexCount,
                expectedCount);
            throw new InvalidOperationException(
                $"Embedding provider returned duplicate or missing indices. DistinctIndexCount: {distinctIndexCount}, ExpectedCount: {expectedCount}.");
        }

        // Sort by index to ensure correct order and extract embeddings
        List<EmbeddingData> sortedData = data.OrderBy(d => d.Index).ToList();
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

            string encodedModel = global::System.Uri.EscapeDataString(model);

            return $"{baseUrl}/openai/deployments/{encodedModel}/embeddings?api-version={_options.EffectiveApiVersion}";
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

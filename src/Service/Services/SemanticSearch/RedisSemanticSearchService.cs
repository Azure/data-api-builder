// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Services.SemanticSearch;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Azure.DataApiBuilder.Service.Services.SemanticSearch;

/// <summary>
/// Resolves semantic candidates by:
/// 1) generating/retrieving an embedding vector for semantic_search input,
/// 2) performing a Redis FT.SEARCH KNN query,
/// 3) extracting SQL column/value pairs from Redis hash/json documents.
/// </summary>
public sealed class RedisSemanticSearchService : ISemanticSearchService
{
    private const string EMBED_ENDPOINT_ENV = "DAB_SEMANTIC_EMBED_ENDPOINT";
    private const string EMBED_API_KEY_ENV = "DAB_SEMANTIC_EMBED_API_KEY";
    private const string VECTOR_SCORE_FIELD = "__vector_score";
    private const string DEFAULT_VECTOR_FIELD = "embedding";

    private readonly RuntimeConfigProvider _runtimeConfigProvider;
    private readonly IMetadataProviderFactory _metadataProviderFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RedisSemanticSearchService> _logger;

    public RedisSemanticSearchService(
        RuntimeConfigProvider runtimeConfigProvider,
        IMetadataProviderFactory metadataProviderFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<RedisSemanticSearchService> logger)
    {
        _runtimeConfigProvider = runtimeConfigProvider;
        _metadataProviderFactory = metadataProviderFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SemanticSearchCandidate>> GetCandidatesAsync(
        string entityName,
        EntitySemanticSearchOptions options,
        IReadOnlyList<string> primaryKeyColumns,
        string semanticSearchValue,
        double similarityThreshold,
        int top)
    {
        RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
        string? connectionString = runtimeConfig.Runtime?.Cache?.Level2?.ConnectionString;

        if (string.IsNullOrWhiteSpace(options.RedisIndexName)
            || string.IsNullOrWhiteSpace(connectionString)
            || top <= 0)
        {
            return [];
        }

        float[] embedding = await GetEmbeddingAsync(semanticSearchValue);
        if (embedding.Length == 0)
        {
            return [];
        }

        string dataSourceName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);
        SourceDefinition sourceDefinition = _metadataProviderFactory.GetMetadataProvider(dataSourceName).GetSourceDefinition(entityName);
        HashSet<string> sourceColumns = new(sourceDefinition.Columns.Keys, StringComparer.OrdinalIgnoreCase);

        try
        {
            using IConnectionMultiplexer multiplexer = await CreateConnectionMultiplexerAsync(connectionString);
            IDatabase db = multiplexer.GetDatabase();

            string vectorFieldName = await ResolveVectorFieldNameAsync(db, options.RedisIndexName!, options.RedisIndexType);
            byte[] vectorBytes = ToRedisVectorBytes(embedding);

            RedisResult rawResult = await db.ExecuteAsync(
                "FT.SEARCH",
                options.RedisIndexName!,
                $"*=>[KNN {top} @{vectorFieldName} $vec AS {VECTOR_SCORE_FIELD}]",
                "PARAMS",
                "2",
                "vec",
                vectorBytes,
                "SORTBY",
                VECTOR_SCORE_FIELD,
                "DIALECT",
                "2",
                "LIMIT",
                "0",
                top.ToString(CultureInfo.InvariantCulture));

            return ParseCandidates(rawResult, options.RedisIndexType, sourceColumns, primaryKeyColumns, similarityThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic search query failed for entity {entityName} and index {indexName}.", entityName, options.RedisIndexName);
            throw new DataApiBuilderException(
                message: $"Semantic search index '{options.RedisIndexName}' for entity '{entityName}' was not found or could not be queried.",
                statusCode: System.Net.HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
        }
    }

    private static byte[] ToRedisVectorBytes(float[] vector)
    {
        byte[] bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private async Task<float[]> GetEmbeddingAsync(string semanticSearchValue)
    {
        if (TryParseVectorText(semanticSearchValue, out float[]? parsedVector) && parsedVector is not null)
        {
            return parsedVector;
        }

        string? endpoint = Environment.GetEnvironmentVariable(EMBED_ENDPOINT_ENV);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return [];
        }

        HttpClient client = _httpClientFactory.CreateClient();
        string? apiKey = Environment.GetEnvironmentVariable(EMBED_API_KEY_ENV);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        string payload = JsonSerializer.Serialize(new Dictionary<string, string> { { "input", semanticSearchValue } });
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using HttpResponseMessage response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        string body = await response.Content.ReadAsStringAsync();
        return TryExtractEmbedding(body, out float[]? embedding) && embedding is not null ? embedding : [];
    }

    private static bool TryParseVectorText(string text, out float[]? vector)
    {
        vector = null;

        try
        {
            using JsonDocument json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind is not JsonValueKind.Array)
            {
                return false;
            }

            List<float> values = [];
            foreach (JsonElement element in json.RootElement.EnumerateArray())
            {
                if (!element.TryGetSingle(out float current))
                {
                    return false;
                }

                values.Add(current);
            }

            vector = values.ToArray();
            return vector.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractEmbedding(string responseBody, out float[]? embedding)
    {
        embedding = null;

        try
        {
            using JsonDocument json = JsonDocument.Parse(responseBody);

            if (json.RootElement.ValueKind is JsonValueKind.Array)
            {
                return TryReadEmbeddingArray(json.RootElement, out embedding);
            }

            if (json.RootElement.ValueKind is JsonValueKind.Object)
            {
                if (json.RootElement.TryGetProperty("embedding", out JsonElement directEmbedding)
                    && TryReadEmbeddingArray(directEmbedding, out embedding))
                {
                    return true;
                }

                if (json.RootElement.TryGetProperty("data", out JsonElement data)
                    && data.ValueKind is JsonValueKind.Array
                    && data.GetArrayLength() > 0)
                {
                    JsonElement first = data[0];
                    if (first.ValueKind is JsonValueKind.Object
                        && first.TryGetProperty("embedding", out JsonElement nestedEmbedding)
                        && TryReadEmbeddingArray(nestedEmbedding, out embedding))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadEmbeddingArray(JsonElement array, out float[]? embedding)
    {
        embedding = null;

        if (array.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        List<float> values = [];
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (!item.TryGetSingle(out float current))
            {
                return false;
            }

            values.Add(current);
        }

        embedding = values.ToArray();
        return embedding.Length > 0;
    }

    private static async Task<IConnectionMultiplexer> CreateConnectionMultiplexerAsync(string connectionString)
    {
        ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);

        if (Startup.ShouldUseEntraAuthForRedis(options))
        {
            options = await options.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());
        }

        return await ConnectionMultiplexer.ConnectAsync(options);
    }

    private static async Task<string> ResolveVectorFieldNameAsync(IDatabase db, string indexName, string redisIndexType)
    {
        try
        {
            RedisResult infoResult = await db.ExecuteAsync("FT.INFO", indexName);
            if (infoResult.IsNull)
            {
                return DEFAULT_VECTOR_FIELD;
            }

            RedisResult[]? infoItems = AsRedisArray(infoResult);
            if (infoItems is not null)
            {
                for (int i = 0; i + 1 < infoItems.Length; i += 2)
                {
                    string? key = infoItems[i].ToString();
                    if (!string.Equals(key, "attributes", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    RedisResult[]? attributes = AsRedisArray(infoItems[i + 1]);
                    if (attributes is null)
                    {
                        continue;
                    }

                    foreach (RedisResult attribute in attributes)
                    {
                        RedisResult[]? tokens = AsRedisArray(attribute);
                        if (tokens is null)
                        {
                            continue;
                        }

                        bool isVector = false;
                        string? identifier = null;
                        string? alias = null;

                        for (int t = 0; t + 1 < tokens.Length; t += 2)
                        {
                            string? tokenKey = tokens[t].ToString();
                            string? tokenValue = tokens[t + 1].ToString();

                            if (string.Equals(tokenKey, "type", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(tokenValue, "VECTOR", StringComparison.OrdinalIgnoreCase))
                            {
                                isVector = true;
                            }
                            else if (string.Equals(tokenKey, "identifier", StringComparison.OrdinalIgnoreCase))
                            {
                                identifier = tokenValue;
                            }
                            else if (string.Equals(tokenKey, "attribute", StringComparison.OrdinalIgnoreCase))
                            {
                                alias = tokenValue;
                            }
                        }

                        if (isVector)
                        {
                            string? selected = string.Equals(redisIndexType, "json", StringComparison.OrdinalIgnoreCase)
                                ? alias ?? identifier
                                : identifier ?? alias;

                            if (!string.IsNullOrWhiteSpace(selected))
                            {
                                return selected.TrimStart('$', '.');
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall back to default vector field name; caller will throw a semantic index error if query fails.
        }

        return DEFAULT_VECTOR_FIELD;
    }

    private static IReadOnlyList<SemanticSearchCandidate> ParseCandidates(
        RedisResult rawResult,
        string redisIndexType,
        HashSet<string> sourceColumns,
        IReadOnlyList<string> primaryKeyColumns,
        double similarityThreshold)
    {
        RedisResult[]? rows = AsRedisArray(rawResult);
        if (rawResult.IsNull || rows is null || rows.Length < 3)
        {
            return [];
        }

        List<SemanticSearchCandidate> results = [];
        for (int i = 1; i + 1 < rows.Length; i += 2)
        {
            RedisResult[]? payload = AsRedisArray(rows[i + 1]);
            if (payload is null)
            {
                continue;
            }

            Dictionary<string, object?> extracted = ExtractDocumentFields(payload, redisIndexType);
            double similarity = TryReadSimilarity(extracted);

            if (similarity < similarityThreshold)
            {
                continue;
            }

            Dictionary<string, object?> sqlColumns = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, object? value) in extracted)
            {
                string normalized = NormalizeFieldName(key);
                if (sourceColumns.Contains(normalized))
                {
                    sqlColumns[normalized] = value;
                }
            }

            if (sqlColumns.Count == 0)
            {
                continue;
            }

            Dictionary<string, object?> primaryKeys = new(StringComparer.OrdinalIgnoreCase);
            bool hasAllPrimaryKeys = true;
            foreach (string pk in primaryKeyColumns)
            {
                if (!sqlColumns.TryGetValue(pk, out object? pkValue) || pkValue is null)
                {
                    hasAllPrimaryKeys = false;
                    break;
                }

                primaryKeys[pk] = pkValue;
            }

            if (!hasAllPrimaryKeys)
            {
                continue;
            }

            results.Add(new SemanticSearchCandidate(primaryKeys, sqlColumns, similarity));
        }

        return results;
    }

    private static RedisResult[]? AsRedisArray(RedisResult result)
    {
        try
        {
            RedisResult[]? value = (RedisResult[]?)result;
            return value;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object?> ExtractDocumentFields(RedisResult[] payload, string redisIndexType)
    {
        Dictionary<string, object?> fields = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i + 1 < payload.Length; i += 2)
        {
            string? key = payload[i].ToString();
            string? value = payload[i + 1].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (string.Equals(redisIndexType, "json", StringComparison.OrdinalIgnoreCase)
                && string.Equals(key, "$", StringComparison.Ordinal))
            {
                MergeJsonDocumentFields(fields, value);
                continue;
            }

            fields[key] = value;
        }

        return fields;
    }

    private static void MergeJsonDocumentFields(Dictionary<string, object?> fields, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return;
            }

            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                fields[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.ToString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => property.Value.ToString()
                };
            }
        }
        catch
        {
            // Ignore malformed JSON payloads and continue with other fields.
        }
    }

    private static double TryReadSimilarity(Dictionary<string, object?> fields)
    {
        if (!fields.TryGetValue(VECTOR_SCORE_FIELD, out object? scoreObj)
            || scoreObj is null
            || !double.TryParse(scoreObj.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rawScore))
        {
            return 0.0;
        }

        // Redis KNN score is distance; normalize to similarity for semantic-threshold semantics.
        double similarity = 1.0 - rawScore;
        if (similarity < 0.0)
        {
            similarity = 0.0;
        }

        if (similarity > 1.0)
        {
            similarity = 1.0;
        }

        return similarity;
    }

    private static string NormalizeFieldName(string field)
    {
        string normalized = field.Trim();

        if (normalized.StartsWith("$.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.Contains('.', StringComparison.Ordinal))
        {
            normalized = normalized[(normalized.LastIndexOf('.') + 1)..];
        }

        return normalized;
    }
}
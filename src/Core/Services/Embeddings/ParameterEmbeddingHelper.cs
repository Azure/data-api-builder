// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Services.Embeddings;

/// <summary>
/// Substitutes text values with embedding vectors for stored procedure parameters
/// marked with auto-embed:true.
///
/// Called before SqlExecuteStructure construction. DAB acts as a pure string-substitution
/// layer: the sproc parameter must be a string-compatible type (NVARCHAR/VARCHAR), and
/// the sproc itself is responsible for any CAST(... AS VECTOR(N)) needed for vector
/// arithmetic.
///
/// Example:
///   Input:  resolvedParams["query_text"] = "wireless headphones"  (user's text)
///   Output: resolvedParams["query_text"] = "[0.012,-0.045,...,0.083]"  (vector JSON string)
///
/// Telemetry: when the helper does meaningful work (has at least one auto-embed param
/// configured), it emits an Activity span and metrics via
/// <see cref="ParameterEmbeddingTelemetryHelper"/>. Early-exit no-ops (no params,
/// no auto-embed params) are not instrumented.
/// </summary>
public static class ParameterEmbeddingHelper
{
    /// <summary>
    /// Convenience overload that resolves the entity's <see cref="ParameterMetadata"/> from
    /// the runtime config by entity name, then delegates to the parameter-list overload.
    ///
    /// All three engine call sites (SqlQueryEngine GraphQL path, SqlQueryEngine REST path,
    /// SqlMutationEngine REST path) follow the same lookup-then-substitute pattern; this
    /// overload centralizes it so the engines don't each carry the boilerplate.
    /// </summary>
    /// <param name="resolvedParams">
    /// The parameter dictionary from the request. Modified in-place: text values for
    /// auto-embed params are replaced with vector JSON strings.
    /// </param>
    /// <param name="runtimeConfig">The active runtime config (resolved via the provider at the call site).</param>
    /// <param name="entityName">Name of the stored-procedure entity whose parameters may need embedding.</param>
    /// <param name="embeddingService">The embedding service to call for text → vector conversion.</param>
    /// <param name="cancellationToken">Cancellation token from the HTTP request.</param>
    /// <param name="logger">Optional logger for structured per-operation diagnostics. Engines pass their own ILogger; tests may omit.</param>
    public static Task SubstituteEmbedParametersAsync(
        IDictionary<string, object?> resolvedParams,
        RuntimeConfig runtimeConfig,
        string entityName,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        Entity entity = runtimeConfig.Entities[entityName];
        string? sprocName = entity.Source.Object;
        string? provider = runtimeConfig.Runtime?.Embeddings?.Provider.ToString().ToLowerInvariant();
        string? model = runtimeConfig.Runtime?.Embeddings?.EffectiveModel;
        return SubstituteEmbedParametersAsync(
            resolvedParams,
            entity.Source.Parameters,
            embeddingService,
            cancellationToken,
            entityName,
            logger,
            sprocName,
            provider,
            model);
    }

    /// <summary>
    /// For each parameter marked auto-embed:true in config, replaces the text value in
    /// resolvedParams with a serialized vector string.
    ///
    /// Implementation uses 3 phases for efficiency when a sproc has multiple auto-embed params:
    ///   1. COLLECT — validate all auto-embed param values, build a list of texts to embed
    ///   2. BATCH   — single TryEmbedBatchAsync call instead of N sequential TryEmbedAsync calls
    ///   3. SUBSTITUTE — serialize each returned vector and write back into resolvedParams
    ///
    /// For the common single-auto-embed-param case, batch of 1 is equivalent to sequential.
    /// For multi-auto-embed sprocs, this saves ~(N-1) × API_LATENCY on cache miss.
    ///
    /// The embedding call goes through EmbeddingService which has built-in FusionCache,
    /// so repeated identical texts return instantly without calling the AI provider.
    /// </summary>
    /// <param name="resolvedParams">
    /// The parameter dictionary from the request (REST body/query string or GraphQL args).
    /// Modified in-place: text values for auto-embed params are replaced with vector JSON strings.
    /// </param>
    /// <param name="configParams">
    /// Parameter metadata from dab-config.json for this entity.
    /// Contains the auto-embed:true flag for each parameter.
    /// </param>
    /// <param name="embeddingService">The embedding service to call for text → vector conversion.</param>
    /// <param name="cancellationToken">Cancellation token from the HTTP request.</param>
    /// <param name="entityName">Optional entity name used as a low-cardinality telemetry tag and log property.</param>
    /// <param name="logger">Optional logger for structured per-operation diagnostics.</param>
    /// <param name="sprocName">Optional stored procedure name for telemetry (passed by the convenience overload).</param>
    /// <param name="provider">Optional embedding provider name for telemetry (e.g., "azure-openai").</param>
    /// <param name="model">Optional embedding model name for telemetry (e.g., "text-embedding-3-small").</param>
    public static async Task SubstituteEmbedParametersAsync(
        IDictionary<string, object?> resolvedParams,
        List<ParameterMetadata>? configParams,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken,
        string? entityName = null,
        ILogger? logger = null,
        string? sprocName = null,
        string? provider = null,
        string? model = null)
    {
        // Nothing to do if no config params defined. Early-exit no-ops are not instrumented.
        if (configParams is null)
        {
            return;
        }

        // Quick exit: are there any auto-embed params at all in config? Also not instrumented.
        bool hasEmbedParams = configParams.Any(p => p.AutoEmbed);
        if (!hasEmbedParams)
        {
            return;
        }

        // From here on, this is a real substitution operation worth observing.
        Stopwatch sw = Stopwatch.StartNew();
        using Activity? activity = ParameterEmbeddingTelemetryHelper.StartSubstituteActivity(entityName, sprocName);

        try
        {
            await SubstituteEmbedParametersCoreAsync(
                resolvedParams, configParams, embeddingService, cancellationToken,
                entityName, logger, sw, activity, provider, model);
        }
        catch (DataApiBuilderException)
        {
            // Per-site emission already recorded a classified outcome. Just rethrow.
            throw;
        }
        catch (Exception unexpectedEx)
        {
            // Defensive: any non-classified exception (NRE, OOM, infrastructure failure)
            // still gets a metric so dashboards/alerts don't go silent. Mirrors Phase 1's
            // EmbeddingService catch-all pattern.
            sw.Stop();
            ParameterEmbeddingTelemetryHelper.RecordFailure(
                activity, entityName, ParameterEmbeddingTelemetryHelper.OUTCOME_UNEXPECTED_ERROR,
                sw.Elapsed.TotalMilliseconds, unexpectedEx);
            logger?.LogError(
                unexpectedEx,
                "Auto-embed substitution failed unexpectedly for entity '{EntityName}'.",
                entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN);
            throw;
        }
    }

    /// <summary>
    /// Inner implementation after the no-op early exits and telemetry scope setup.
    /// Split out so the outer method can wrap this in a defensive try/catch for
    /// telemetry continuity.
    /// </summary>
    private static async Task SubstituteEmbedParametersCoreAsync(
        IDictionary<string, object?> resolvedParams,
        List<ParameterMetadata> configParams,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken,
        string? entityName,
        ILogger? logger,
        Stopwatch sw,
        Activity? activity,
        string? provider,
        string? model)
    {
        // If we have auto-embed params in config but the embedding service is disabled
        // (NullEmbeddingService injected because runtime.embeddings is absent or disabled),
        // fail loudly. This is the runtime counterpart of the startup config validation
        // (see ValidateEmbedParameters) and catches scenarios where the service was
        // toggled off after startup or where a hot-reload changed the embeddings state.
        if (!embeddingService.IsEnabled)
        {
            DataApiBuilderException disabledEx = new(
                message: "An auto-embed parameter is configured but the embedding service is not available. Verify runtime.embeddings is configured and enabled.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            sw.Stop();
            ParameterEmbeddingTelemetryHelper.RecordFailure(
                activity, entityName, ParameterEmbeddingTelemetryHelper.OUTCOME_SERVICE_DISABLED,
                sw.Elapsed.TotalMilliseconds, disabledEx);
            logger?.LogError(
                "Auto-embed substitution failed for entity '{EntityName}': embedding service is not available.",
                entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN);
            throw disabledEx;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 1: COLLECT — validate each auto-embed param's value and gather texts
        // ─────────────────────────────────────────────────────────────────────
        // We validate eagerly (per-param) so error messages stay specific:
        //   "Parameter 'foo' has auto-embed:true but received a non-string value..."
        // rather than the batch-level "embed failed for params X, Y, Z."
        //
        // After this phase: embedRequests has (paramName, text) for every auto-embed
        // param the user supplied. Param metadata defines order; we preserve it.

        List<(string paramName, string text)> embedRequests = new();

        foreach (ParameterMetadata configParam in configParams)
        {
            // Skip non-auto-embed params — they pass through unchanged
            // Example: "top_k" has AutoEmbed=false → skip
            if (!configParam.AutoEmbed)
            {
                continue;
            }

            // Handle auto-embed params not supplied in the request.
            // Per spec #3331 Value behavior:
            //   - If the param has a configured default that resolves to a non-empty string,
            //     inject it and embed it.
            //   - If the default is null/empty/whitespace, inject "" (skip embedding).
            //   - If no default at all, use existing DAB behavior (SQL Server will raise
            //     "expects parameter X which was not supplied" if required).
            if (!resolvedParams.TryGetValue(configParam.Name, out object? value))
            {
                if (configParam.Default is not null)
                {
                    string defaultText = configParam.Default.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(defaultText))
                    {
                        resolvedParams[configParam.Name] = defaultText;
                        value = defaultText;
                        logger?.LogDebug(
                            "Auto-embed parameter '{ParamName}' on entity '{EntityName}': caller omitted, using configured default.",
                            configParam.Name,
                            entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN);
                    }
                    else
                    {
                        resolvedParams[configParam.Name] = string.Empty;
                        logger?.LogDebug(
                            "Auto-embed parameter '{ParamName}' on entity '{EntityName}': default is empty, passing empty string.",
                            configParam.Name,
                            entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN);
                        continue;
                    }
                }
                else
                {
                    continue;
                }
            }

            // Validate type and extract text (throws 400 for non-strings)
            string? text;
            try
            {
                text = ExtractTextValue(configParam.Name, value);
            }
            catch (DataApiBuilderException nonStringEx)
            {
                sw.Stop();
                ParameterEmbeddingTelemetryHelper.RecordFailure(
                    activity, entityName, ParameterEmbeddingTelemetryHelper.OUTCOME_NON_STRING,
                    sw.Elapsed.TotalMilliseconds, nonStringEx);
                logger?.LogWarning(
                    "Auto-embed parameter '{ParamName}' on entity '{EntityName}' rejected: {Reason}",
                    configParam.Name,
                    entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN,
                    nonStringEx.Message);
                throw;
            }

            // Per spec #3331 Value behavior: null, empty, and whitespace-only values
            // skip embedding and pass "" to the stored procedure. The sproc decides
            // how to handle empty input (e.g., return empty results, raise SQL error).
            if (string.IsNullOrWhiteSpace(text))
            {
                resolvedParams[configParam.Name] = string.Empty;
                logger?.LogDebug(
                    "Auto-embed parameter '{ParamName}' on entity '{EntityName}': value is null/empty/whitespace, passing empty string to sproc.",
                    configParam.Name,
                    entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN);
                continue;
            }

            embedRequests.Add((configParam.Name, text!));
        }

        // No embed param values supplied in this request — nothing to embed. This is a
        // legitimate no-op (e.g., sproc with auto-embed param + non-auto-embed params,
        // client called only the latter), so we record it as a successful 0-param substitution.
        if (embedRequests.Count == 0)
        {
            sw.Stop();
            ParameterEmbeddingTelemetryHelper.RecordSuccess(activity, entityName, paramCount: 0, sw.Elapsed.TotalMilliseconds);
            return;
        }

        logger?.LogDebug(
            "Substituting {Count} auto-embed parameter(s) for entity '{EntityName}'.",
            embedRequests.Count,
            entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN);

        // Enrich the activity span with parameter names and provider/model metadata
        // so trace viewers show which params were embedded and which provider was used.
        ParameterEmbeddingTelemetryHelper.SetActivityParamAndProviderTags(
            activity,
            embedRequests.Select(r => r.paramName),
            provider,
            model);

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 2: BATCH — single API call for all texts at once
        // ─────────────────────────────────────────────────────────────────────
        // EmbeddingService.TryEmbedBatchAsync (Phase 1 infra) does its own per-text
        // FusionCache check. Texts that hit the cache don't trigger API calls;
        // only uncached texts go to Azure OpenAI in a single batched request.
        //
        // For 1 text: equivalent to TryEmbedAsync (no overhead).
        // For N texts: 1 API call instead of N sequential ones (saves ~(N-1) × latency).

        string[] texts = embedRequests.Select(r => r.text).ToArray();
        EmbeddingBatchResult batchResult = await embeddingService.TryEmbedBatchAsync(texts, cancellationToken);

        if (!batchResult.Success || batchResult.Embeddings is null)
        {
            // Batch failure: include the provider's ErrorMessage when available so the caller
            // sees the actual reason (e.g., quota exhausted, model not found, authentication
            // failed) rather than only the generic "Failed to generate embeddings" line.
            // Per-param specificity is lost at the batch level, so naming all involved params
            // helps identify the request context.
            string paramNames = string.Join(", ", embedRequests.Select(r => $"'{r.paramName}'"));
            string providerDetail = string.IsNullOrWhiteSpace(batchResult.ErrorMessage)
                ? string.Empty
                : $" Provider error: {batchResult.ErrorMessage}";
            DataApiBuilderException batchEx = new(
                message: $"Failed to generate embeddings for parameter(s) {paramNames}.{providerDetail}",
                statusCode: HttpStatusCode.BadGateway,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            sw.Stop();
            ParameterEmbeddingTelemetryHelper.RecordFailure(
                activity, entityName, ParameterEmbeddingTelemetryHelper.OUTCOME_BATCH_FAILURE,
                sw.Elapsed.TotalMilliseconds, batchEx);
            logger?.LogError(
                "Embedding batch failed for entity '{EntityName}', parameter(s) {ParamNames}.{ProviderDetail}",
                entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN,
                paramNames,
                providerDetail);
            throw batchEx;
        }

        // Defensive: TryEmbedBatchAsync should return one embedding per input text
        if (batchResult.Embeddings.Length != embedRequests.Count)
        {
            DataApiBuilderException lengthEx = new(
                message: $"Embedding service returned {batchResult.Embeddings.Length} embeddings but {embedRequests.Count} were requested.",
                statusCode: HttpStatusCode.BadGateway,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            sw.Stop();
            ParameterEmbeddingTelemetryHelper.RecordFailure(
                activity, entityName, ParameterEmbeddingTelemetryHelper.OUTCOME_PROVIDER_INVALID_RESPONSE,
                sw.Elapsed.TotalMilliseconds, lengthEx);
            throw lengthEx;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 3: SUBSTITUTE — serialize each vector and write back into resolvedParams
        // ─────────────────────────────────────────────────────────────────────
        // Order is preserved: embedRequests[i] corresponds to batchResult.Embeddings[i].

        for (int i = 0; i < embedRequests.Count; i++)
        {
            float[] embedding = batchResult.Embeddings[i];
            if (embedding is null || embedding.Length == 0)
            {
                DataApiBuilderException emptyVecEx = new(
                    message: $"Embedding service returned an empty vector for parameter '{embedRequests[i].paramName}'.",
                    statusCode: HttpStatusCode.BadGateway,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                sw.Stop();
                ParameterEmbeddingTelemetryHelper.RecordFailure(
                    activity, entityName, ParameterEmbeddingTelemetryHelper.OUTCOME_PROVIDER_INVALID_RESPONSE,
                    sw.Elapsed.TotalMilliseconds, emptyVecEx);
                throw emptyVecEx;
            }

            // Serialize float[] to a JSON array string (e.g., "[0.012,-0.045,0.083]"). The sproc
            // receives this as a plain string and is responsible for any conversion to a native
            // vector type (e.g., CAST(@param AS VECTOR(N)) on Azure SQL).
            // Example: float[] { 0.012f, -0.045f, 0.083f } → "[0.012,-0.045,0.083]"
            //
            // Uses InvariantCulture to prevent European locales from using comma as decimal separator
            // (e.g., German locale would produce "0,012" instead of "0.012" → invalid JSON)
            //
            // Uses "G9" format specifier (NOT "G" or "R"):
            //   - "G" defaults to G7 for Single, which is NOT round-trippable (~30% of values lose precision)
            //   - "R" is documented to fail round-trip in some cases for Single
            //     (per Microsoft docs: "We recommend that you use the G9 format specifier instead.")
            //   - "G9" guarantees the string can be parsed back to the exact same float
            //   - Embeddings are precision-sensitive — even tiny drift affects cosine similarity scores
            string vectorJson = "["
                + string.Join(",", embedding.Select(f => f.ToString("G9", CultureInfo.InvariantCulture)))
                + "]";

            // Replace the text value with the vector JSON string in-place. SqlExecuteStructure
            // sees this as a plain String value and passes it through to the sproc, which is
            // responsible for any conversion to a native vector type if needed.
            resolvedParams[embedRequests[i].paramName] = vectorJson;
        }

        sw.Stop();
        ParameterEmbeddingTelemetryHelper.RecordSuccess(activity, entityName, embedRequests.Count, sw.Elapsed.TotalMilliseconds);
        logger?.LogDebug(
            "Substituted {Count} auto-embed parameter(s) for entity '{EntityName}' in {DurationMs}ms.",
            embedRequests.Count,
            entityName ?? ParameterEmbeddingTelemetryHelper.ENTITY_UNKNOWN,
            sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Validates that the parameter value is a string (or JsonElement of kind String) and extracts it.
    ///
    /// Azure OpenAI's embedding API only accepts strings as input — passing a number,
    /// boolean, array, or object would either be silently stringified into garbage
    /// (e.g., "System.Object[]") or rejected by the API with a confusing error.
    ///
    /// DAB delivers parameter values as either System.String OR System.Text.Json.JsonElement
    /// (the JSON parser wraps body values in JsonElement). We accept both string and
    /// JsonElement-of-kind-String, and reject all other types with a clear 400.
    ///
    /// Example FAIL: { "query_vector": 12345 } → JsonElement(Number) → 400 "must be a string"
    /// Example FAIL: { "query_vector": true } → JsonElement(True) → 400 "must be a string"
    /// Example FAIL: { "query_vector": ["a","b"] } → JsonElement(Array) → 400 "must be a string"
    /// Example FAIL: { "query_vector": {"foo":"bar"} } → JsonElement(Object) → 400 "must be a string"
    /// Example PASS: { "query_vector": "headphones" } → JsonElement(String) or string → returns text
    ///
    /// Note: GraphQL is automatically protected since the sproc param must be a
    /// string-compatible type (validated at startup), and so the embed param is exposed
    /// as a GraphQL String — non-strings are rejected by the GraphQL parser before
    /// reaching this code.
    /// </summary>
    private static string? ExtractTextValue(string paramName, object? value)
    {
        if (value is string s)
        {
            return s;
        }

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                return jsonElement.GetString();
            }

            if (jsonElement.ValueKind == JsonValueKind.Null)
            {
                return null;  // Will be caught by IsNullOrWhiteSpace in caller
            }

            throw new DataApiBuilderException(
                message: $"Parameter '{paramName}' has 'auto-embed: true' but received a non-string JSON value of kind '{jsonElement.ValueKind}'. Auto-embed parameters must be JSON strings.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
        }

        if (value is not null)
        {
            throw new DataApiBuilderException(
                message: $"Parameter '{paramName}' has 'auto-embed: true' but received a non-string value of type '{value.GetType().Name}'. Auto-embed parameters must be JSON strings.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
        }

        return null;  // null value will be caught by IsNullOrWhiteSpace in caller
    }
}

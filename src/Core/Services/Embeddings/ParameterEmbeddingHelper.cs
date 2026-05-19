// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Services.Embeddings;

/// <summary>
/// Substitutes text values with embedding vectors for stored procedure parameters marked with auto-embed:true.
///
/// Called before SqlExecuteStructure construction — the substituted values flow through
/// the normal String type conversion path (thanks to the metadata type override that
/// changes VECTOR params from Byte[] to String during startup).
///
/// Example:
///   Input:  resolvedParams["query_vector"] = "wireless headphones"  (user's text)
///   Output: resolvedParams["query_vector"] = "[0.012,-0.045,...,0.083]"  (vector JSON string)
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
    public static Task SubstituteEmbedParametersAsync(
        IDictionary<string, object?> resolvedParams,
        RuntimeConfig runtimeConfig,
        string entityName,
        IEmbeddingService? embeddingService,
        CancellationToken cancellationToken)
    {
        Entity entity = runtimeConfig.Entities[entityName];
        return SubstituteEmbedParametersAsync(
            resolvedParams,
            entity.Source.Parameters,
            embeddingService,
            cancellationToken);
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
    public static async Task SubstituteEmbedParametersAsync(
        IDictionary<string, object?> resolvedParams,
        List<ParameterMetadata>? configParams,
        IEmbeddingService? embeddingService,
        CancellationToken cancellationToken)
    {
        // Nothing to do if no config params defined
        if (configParams is null)
        {
            return;
        }

        // Quick exit: are there any auto-embed params at all in config?
        bool hasEmbedParams = configParams.Any(p => p.AutoEmbed);
        if (!hasEmbedParams)
        {
            return;
        }

        // If we have auto-embed params in config but no embedding service, fail loudly.
        // This catches DI misconfiguration and future code paths that construct engines
        // without the service. Without this check, the silent-skip behavior would send
        // raw text to SQL, producing confusing errors or silently wrong results.
        //
        // Note: Startup config validation already requires runtime.embeddings to be
        // configured and enabled when auto-embed:true is present (see ValidateEmbedParameters).
        // This is defense-in-depth for unexpected DI states at runtime.
        if (embeddingService is null)
        {
            throw new DataApiBuilderException(
                message: "An auto-embed parameter is configured but the embedding service is not available. Verify runtime.embeddings is configured and enabled.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
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

            // Skip auto-embed params not supplied in the request. We don't enforce
            // required-ness here because DAB's request validation for sprocs only
            // checks for extra fields, not missing ones — see RequestValidator
            // .ValidateStoredProcedureRequestContext, which delegates required-param
            // detection to SQL Server (no easy way to read default-value metadata
            // from sys.parameters in a portable way).
            //
            // When a required auto-embed param is missing entirely, SqlExecuteStructure
            // also won't bind it, and SQL Server returns "expects parameter X, which
            // was not supplied." MsSqlDbExceptionParser translates that to a 400
            // DatabaseInputError for the client. So missing values still produce a
            // clear, actionable error — just via the SQL-error-relay path rather
            // than this helper.
            //
            // (Explicit null or empty string DOES reach this helper, via the
            // IsNullOrWhiteSpace check below — that path produces our own 400 with
            // the friendlier "has 'auto-embed: true' but the provided text is empty or
            // whitespace" message.)
            if (!resolvedParams.TryGetValue(configParam.Name, out object? value))
            {
                continue;
            }

            // Validate type and extract text (throws 400 for non-strings)
            string? text = ExtractTextValue(configParam.Name, value);

            // Validate non-empty (throws 400 for empty/whitespace/null)
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new DataApiBuilderException(
                    message: $"Parameter '{configParam.Name}' has 'auto-embed: true' but the provided text is empty or whitespace.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            embedRequests.Add((configParam.Name, text!));
        }

        // No embed param values supplied in this request — nothing to embed
        if (embedRequests.Count == 0)
        {
            return;
        }

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
            throw new DataApiBuilderException(
                message: $"Failed to generate embeddings for parameter(s) {paramNames}.{providerDetail}",
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
        }

        // Defensive: TryEmbedBatchAsync should return one embedding per input text
        if (batchResult.Embeddings.Length != embedRequests.Count)
        {
            throw new DataApiBuilderException(
                message: $"Embedding service returned {batchResult.Embeddings.Length} embeddings but {embedRequests.Count} were requested.",
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
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
                throw new DataApiBuilderException(
                    message: $"Embedding service returned an empty vector for parameter '{embedRequests[i].paramName}'.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            // Serialize float[] to JSON string that Azure SQL accepts as VECTOR
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

            // Replace the text value with the vector string in-place
            // SqlExecuteStructure will see this as a String value (thanks to metadata override)
            // and pass it through to SQL, which auto-casts to VECTOR(N)
            resolvedParams[embedRequests[i].paramName] = vectorJson;
        }
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
    /// Note: GraphQL is automatically protected since the embed param is exposed as
    /// GraphQL String type (via Stage 2 metadata override) — non-strings are rejected
    /// by the GraphQL parser before reaching this code.
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

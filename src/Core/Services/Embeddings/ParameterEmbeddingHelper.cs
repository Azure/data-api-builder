// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Services.Embeddings;

/// <summary>
/// Substitutes text values with embedding vectors for stored procedure parameters marked with embed:true.
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
    /// For each parameter marked embed:true in config, replaces the text value in
    /// resolvedParams with a serialized vector string.
    ///
    /// The embedding call goes through EmbeddingService which has built-in FusionCache,
    /// so repeated identical texts return instantly without calling the AI provider.
    /// </summary>
    /// <param name="resolvedParams">
    /// The parameter dictionary from the request (REST body/query string or GraphQL args).
    /// Modified in-place: text values for embed params are replaced with vector JSON strings.
    /// </param>
    /// <param name="configParams">
    /// Parameter metadata from dab-config.json for this entity.
    /// Contains the embed:true flag for each parameter.
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

        // Quick exit: are there any embed params at all in config?
        bool hasEmbedParams = configParams.Any(p => p.Embed);
        if (!hasEmbedParams)
        {
            return;
        }

        foreach (ParameterMetadata configParam in configParams)
        {
            // Skip non-embed params — they pass through unchanged
            // Example: "top_k" has Embed=false → skip
            if (!configParam.Embed)
            {
                continue;
            }

            // Check if the request provided this parameter
            // If not provided, DAB's existing required-param validation will handle it
            // (and an embed:true param without a value in this request doesn't need
            // the embedding service — only the params actually being substituted do)
            if (!resolvedParams.TryGetValue(configParam.Name, out object? value))
            {
                continue;
            }

            // If we have an embed param value to substitute but no embedding service,
            // fail loudly. Without this check, the silent-skip behavior would send raw
            // text to SQL, producing confusing errors or silently wrong results.
            //
            // This catches:
            //   - DI misconfiguration (service not registered when embed params exist)
            //   - Future code paths that construct engines without the service
            //
            // Note: This check is scoped to "this request actually has an embed param
            // value" — a request that omits the embed param won't fail here, so optional
            // embed params don't break when the embedding service is unavailable.
            if (embeddingService is null)
            {
                throw new DataApiBuilderException(
                    message: $"Parameter '{configParam.Name}' has 'embed: true' but the embedding service is not available. Verify runtime.embeddings is configured and enabled.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            // Validate: embed parameters must be strings.
            // Azure OpenAI's embedding API only accepts strings as input — passing a number,
            // boolean, array, or object would either be silently stringified into garbage
            // (e.g., "System.Object[]") or rejected by the API with a confusing error.
            //
            // DAB delivers parameter values as either System.String OR System.Text.Json.JsonElement
            // (the JSON parser wraps body values in JsonElement). We accept both string and
            // JsonElement-of-kind-String, and reject all other types with a clear 400.
            //
            // Example FAIL: { "query_vector": 12345 } → JsonElement(Number) → 400 "must be a string"
            // Example FAIL: { "query_vector": true } → JsonElement(True) → 400 "must be a string"
            // Example FAIL: { "query_vector": ["a","b"] } → JsonElement(Array) → 400 "must be a string"
            // Example FAIL: { "query_vector": {"foo":"bar"} } → JsonElement(Object) → 400 "must be a string"
            // Example PASS: { "query_vector": "headphones" } → JsonElement(String) or string → proceed
            //
            // Note: GraphQL is automatically protected since the embed param is exposed as
            // GraphQL String type (via Stage 2 metadata override) — non-strings are rejected
            // by the GraphQL parser before reaching this code.
            string? text = null;
            if (value is string s)
            {
                text = s;
            }
            else if (value is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    text = jsonElement.GetString();
                }
                else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Null)
                {
                    text = null;  // Will be caught by IsNullOrWhiteSpace below
                }
                else
                {
                    throw new DataApiBuilderException(
                        message: $"Parameter '{configParam.Name}' has 'embed: true' but received a non-string JSON value of kind '{jsonElement.ValueKind}'. Embed parameters must be JSON strings.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
            else if (value is not null)
            {
                throw new DataApiBuilderException(
                    message: $"Parameter '{configParam.Name}' has 'embed: true' but received a non-string value of type '{value.GetType().Name}'. Embed parameters must be JSON strings.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // Validate: embed params must have non-empty text
            // Example FAIL: { "query_vector": "" } → 400 error
            // Example FAIL: { "query_vector": "   " } → 400 error
            // Example FAIL: { "query_vector": null } → 400 error (null treated as empty)
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new DataApiBuilderException(
                    message: $"Parameter '{configParam.Name}' has 'embed: true' but the provided text is empty or whitespace.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // Call EmbeddingService to convert text → float[1536]
            // The service has built-in FusionCache (L1 + optional L2 Redis):
            //   First call for "wireless headphones" → calls Azure OpenAI API (~200-500ms)
            //   Second call for same text → cache hit, returns instantly
            EmbeddingResult result = await embeddingService.TryEmbedAsync(text, cancellationToken);

            if (!result.Success || result.Embedding is null)
            {
                throw new DataApiBuilderException(
                    message: $"Failed to generate embedding for parameter '{configParam.Name}'.",
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
                + string.Join(",", result.Embedding.Select(f => f.ToString("G9", CultureInfo.InvariantCulture)))
                + "]";

            // Replace the text value with the vector string in-place
            // SqlExecuteStructure will see this as a String value (thanks to metadata override)
            // and pass it through to SQL, which auto-casts to VECTOR(N)
            resolvedParams[configParam.Name] = vectorJson;
        }
    }
}

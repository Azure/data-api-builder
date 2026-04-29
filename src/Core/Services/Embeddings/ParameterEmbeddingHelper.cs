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
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        // Nothing to do if no config params defined
        if (configParams is null)
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
            if (!resolvedParams.TryGetValue(configParam.Name, out object? value))
            {
                continue;
            }

            // Validate: embed params must have non-empty text
            // Example FAIL: { "query_vector": "" } → 400 error
            // Example FAIL: { "query_vector": "   " } → 400 error
            string? text = value?.ToString();
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
            string vectorJson = "["
                + string.Join(",", result.Embedding.Select(f => f.ToString("G", CultureInfo.InvariantCulture)))
                + "]";

            // Replace the text value with the vector string in-place
            // SqlExecuteStructure will see this as a String value (thanks to metadata override)
            // and pass it through to SQL, which auto-casts to VECTOR(N)
            resolvedParams[configParam.Name] = vectorJson;
        }
    }
}

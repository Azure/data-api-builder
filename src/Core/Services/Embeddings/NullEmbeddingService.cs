// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Services.Embeddings;

/// <summary>
/// Null-object implementation of <see cref="IEmbeddingService"/> used when
/// runtime.embeddings is absent or disabled.
///
/// Registered unconditionally in the DI container so consumers can take
/// <c>IEmbeddingService</c> as a non-nullable dependency. DI misconfiguration
/// (e.g., forgetting to register the service) now surfaces at startup as a
/// constructor-injection failure rather than as a null-reference at first
/// request.
///
/// Behavior:
///   - <see cref="IsEnabled"/> returns false. Consumers that gate their logic
///     on this property (e.g., HealthCheckHelper skips the embedding probe)
///     behave as if no service were configured.
///   - <see cref="TryEmbedAsync"/> / <see cref="TryEmbedBatchAsync"/> return
///     a failure <see cref="EmbeddingResult"/> with an actionable error
///     message. Callers that already handle batch failures (e.g.,
///     ParameterEmbeddingHelper) surface this through their existing error
///     paths.
///   - <see cref="EmbedAsync"/> / <see cref="EmbedBatchAsync"/> throw
///     <see cref="InvalidOperationException"/>. The non-Try variants are
///     contract-required to produce a vector, so a hard failure is the only
///     correct response when no real service is available.
///
/// Use <see cref="Instance"/> for both DI registration and test fixtures.
/// </summary>
public sealed class NullEmbeddingService : IEmbeddingService
{
    private const string DisabledMessage =
        "Embedding service is not configured. Set runtime.embeddings.enabled = true in dab-config.json to enable embeddings.";

    /// <summary>
    /// Shared singleton instance. Use this for DI registration and as a
    /// stand-in for the real service in unit/integration test fixtures.
    /// </summary>
    public static readonly NullEmbeddingService Instance = new();

    private NullEmbeddingService() { }

    /// <inheritdoc/>
    public bool IsEnabled => false;

    /// <inheritdoc/>
    public Task<EmbeddingResult> TryEmbedAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult(new EmbeddingResult(Success: false, Embedding: null, ErrorMessage: DisabledMessage));

    /// <inheritdoc/>
    public Task<EmbeddingBatchResult> TryEmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default)
        => Task.FromResult(new EmbeddingBatchResult(Success: false, Embeddings: null, ErrorMessage: DisabledMessage));

    /// <inheritdoc/>
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(DisabledMessage);

    /// <inheritdoc/>
    public Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(DisabledMessage);
}

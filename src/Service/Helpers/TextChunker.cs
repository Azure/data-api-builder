// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

namespace Azure.DataApiBuilder.Service.Helpers;

/// <summary>
/// Static helper for splitting text into overlapping chunks before embedding.
/// Encapsulates the chunking algorithm so it can be tested and reused independently of the controller.
/// </summary>
public static class TextChunker
{
    /// <summary>
    /// Splits <paramref name="text"/> into chunks of at most <paramref name="chunkSize"/> characters,
    /// with each consecutive chunk overlapping by <paramref name="overlap"/> characters.
    /// Returns an empty array for null or empty input.
    /// The step size is always at least 1 (<c>Math.Max(1, chunkSize - overlap)</c>),
    /// so this method always terminates regardless of the overlap value.
    /// </summary>
    public static string[] ChunkText(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        // Guarantee at least one character of forward progress per iteration.
        int step = Math.Max(1, chunkSize - overlap);

        if (text.Length <= chunkSize)
        {
            return new[] { text };
        }

        List<string> chunks = new();
        int position = 0;

        while (position < text.Length)
        {
            int remaining = text.Length - position;
            chunks.Add(text.Substring(position, Math.Min(chunkSize, remaining)));
            position += step;
        }

        return chunks.ToArray();
    }

    /// <summary>
    /// Splits text into chunks based on the provided <see cref="EmbeddingsChunkingOptions"/>.
    /// When chunking is disabled or options are null, returns the text as a single-element array.
    /// Uses <see cref="EmbeddingsChunkingOptions.EffectiveSizeChars"/> to guarantee step &gt;= 1.
    /// </summary>
    public static string[] ChunkText(string text, EmbeddingsChunkingOptions? chunkingOptions)
    {
        if (chunkingOptions is null || !chunkingOptions.Enabled)
        {
            return new[] { text };
        }

        // EffectiveSizeChars = Math.Max(SizeChars, OverlapChars + 1), guaranteeing step >= 1.
        return ChunkText(text, chunkingOptions.EffectiveSizeChars, chunkingOptions.OverlapChars);
    }
}

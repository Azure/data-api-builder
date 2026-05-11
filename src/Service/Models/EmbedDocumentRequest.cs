// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Service.Models;

/// <summary>
/// Request model for a single document in a batch embedding request.
/// </summary>
public record EmbedDocumentRequest
{
    /// <summary>
    /// Unique key/identifier for this document.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// The text content to embed.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

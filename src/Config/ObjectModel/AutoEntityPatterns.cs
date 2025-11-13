// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Defines the pattern matching rules for auto-entities.
/// </summary>
/// <param name="Include">T-SQL LIKE pattern to include database objects (default: null)</param>
/// <param name="Exclude">T-SQL LIKE pattern to exclude database objects (default: null)</param>
/// <param name="Name">Interpolation syntax for entity naming (must be unique, default: null)</param>
public record AutoEntityPatterns(
    [property: JsonPropertyName("include")] string? Include = null,
    [property: JsonPropertyName("exclude")] string? Exclude = null,
    [property: JsonPropertyName("name")] string? Name = null
);

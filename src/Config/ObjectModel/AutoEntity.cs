// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Defines an individual auto-entity definition with patterns, template, and permissions.
/// </summary>
/// <param name="Patterns">Pattern matching rules for including/excluding database objects</param>
/// <param name="Template">Template configuration for generated entities</param>
/// <param name="Permissions">Permissions configuration for generated entities (at least one required)</param>
public record AutoEntity(
    [property: JsonPropertyName("patterns")] AutoEntityPatterns Patterns,
    [property: JsonPropertyName("template")] AutoEntityTemplate Template,
    [property: JsonPropertyName("permissions")] EntityPermission[] Permissions
);

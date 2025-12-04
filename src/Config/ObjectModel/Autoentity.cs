// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Defines an individual auto-entity definition with patterns, template, and permissions.
/// </summary>
/// <param name="Patterns">Pattern matching rules for including/excluding database objects</param>
/// <param name="Template">Template configuration for generated entities</param>
/// <param name="Permissions">Permissions configuration for generated entities (at least one required)</param>
public record Autoentity
{
    public AutoentityPatterns Patterns { get; init; }
    public AutoentityTemplate Template { get; init; }
    public EntityPermission[] Permissions { get; init; }

    [JsonConstructor]
    public Autoentity(
        AutoentityPatterns? Patterns,
        AutoentityTemplate? Template,
        EntityPermission[] Permissions)
    {
        this.Patterns = Patterns ?? new AutoentityPatterns();

        this.Template = Template ?? new AutoentityTemplate();

        this.Permissions = Permissions;
    }
}

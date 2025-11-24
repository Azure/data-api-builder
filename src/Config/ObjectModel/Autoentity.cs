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
        EntityPermission[]? Permissions)
    {
        if (Patterns is not null)
        {
            this.Patterns = Patterns;
        }
        else
        {
            this.Patterns = new AutoentityPatterns();
        }

        if (Template is not null)
        {
            this.Template = Template;
        }
        else
        {
            this.Template = new AutoentityTemplate();
        }

        if (Permissions is not null)
        {
            this.Permissions = Permissions;
        }
        else
        {
            this.Permissions = Array.Empty<EntityPermission>();
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write permissions
    /// property and value to the runtime config file.
    /// When user doesn't provide the permissions property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a permissions
    /// property/value specified would be interpreted by DAB as "user explicitly set permissions."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Permissions))]
    public bool UserProvidedPermissionsOptions { get; init; } = false;
}

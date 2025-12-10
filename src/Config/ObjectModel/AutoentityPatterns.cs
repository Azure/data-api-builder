// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Defines the pattern matching rules for auto-entities.
/// </summary>
/// <param name="Include">T-SQL LIKE pattern to include database objects</param>
/// <param name="Exclude">T-SQL LIKE pattern to exclude database objects</param>
/// <param name="Name">Interpolation syntax for entity naming (must be unique for each generated entity)</param>
public record AutoentityPatterns
{
    public string[] Include { get; init; }
    public string[] Exclude { get; init; }
    public string Name { get; init; }

    [JsonConstructor]
    public AutoentityPatterns(
        string[]? Include = null,
        string[]? Exclude = null,
        string? Name = null)
    {
        if (Include is not null)
        {
            this.Include = Include;
            UserProvidedIncludeOptions = true;
        }
        else
        {
            this.Include = ["%.%"];
        }

        if (Exclude is not null)
        {
            this.Exclude = Exclude;
            UserProvidedExcludeOptions = true;
        }
        else
        {
            this.Exclude = [];
        }

        if (!string.IsNullOrWhiteSpace(Name))
        {
            this.Name = Name;
            UserProvidedNameOptions = true;
        }
        else
        {
            this.Name = "{object}";
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write include
    /// property and value to the runtime config file.
    /// When user doesn't provide the include property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a include
    /// property/value specified would be interpreted by DAB as "user explicitly set include."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Include))]
    public bool UserProvidedIncludeOptions { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write exclude
    /// property and value to the runtime config file.
    /// When user doesn't provide the exclude property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a exclude
    /// property/value specified would be interpreted by DAB as "user explicitly set exclude."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Exclude))]
    public bool UserProvidedExcludeOptions { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write name
    /// property and value to the runtime config file.
    /// When user doesn't provide the name property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a name
    /// property/value specified would be interpreted by DAB as "user explicitly set name."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Name))]
    public bool UserProvidedNameOptions { get; init; } = false;
}

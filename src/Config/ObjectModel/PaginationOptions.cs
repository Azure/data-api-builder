// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Pagination options for the dab setup.
/// Properties are nullable to support DAB CLI merge config
/// expected behavior.
/// </summary>
public record PaginationOptions
{
    /// <summary>
    /// Default page size.
    /// </summary>
    public const int DEFAULT_PAGE_SIZE = 100;

    /// <summary>
    /// Max page size.
    /// </summary>
    public const int MAX_PAGE_SIZE = 100000;

    /// <summary>
    /// The default page size for pagination.
    /// </summary>
    [JsonPropertyName("default-page-size")]
    public uint? DefaultPageSize { get; init; } = null;

    /// <summary>
    /// The max page size for pagination.
    /// </summary>
    [JsonPropertyName("max-page-size")]
    public uint? MaxPageSize { get; init; } = null;

    [JsonConstructor]
    public PaginationOptions(int? DefaultPageSize = null, int? MaxPageSize = null)
    {
        if (MaxPageSize is not null)
        {
            this.MaxPageSize = MaxPageSize == -1 ? UInt32.MaxValue : (uint)MaxPageSize;
            UserProvidedMaxPageSize = true;
        }
        else
        {
            this.MaxPageSize = MAX_PAGE_SIZE;
        }

        if (DefaultPageSize is not null)
        {
            this.DefaultPageSize = DefaultPageSize == -1 ? (uint)this.MaxPageSize : (uint)DefaultPageSize;
            UserProvidedDefaultPageSize = true;
        }
        else
        {
            this.DefaultPageSize = DEFAULT_PAGE_SIZE;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write ttl-seconds
    /// property and value to the runtime config file.
    /// When user doesn't provide the default-page-size property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a default-page-size
    /// property/value specified would be interpreted by DAB as "user explicitly set ttl."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DefaultPageSize))]
    public bool UserProvidedDefaultPageSize { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write max-page-size
    /// property and value to the runtime config file.
    /// When user doesn't provide the max-page-size property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a max-page-size
    /// property/value specified would be interpreted by DAB as "user explicitly set ttl."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(MaxPageSize))]
    public bool UserProvidedMaxPageSize { get; init; } = false;

}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Service.Exceptions;

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
    public const uint DEFAULT_PAGE_SIZE = 100;

    /// <summary>
    /// Max page size.
    /// </summary>
    public const uint MAX_PAGE_SIZE = 100000;

    /// <summary>
    /// The default page size for pagination.
    /// </summary>
    [JsonPropertyName("default-page-size")]
    public int? DefaultPageSize { get; init; } = null;

    /// <summary>
    /// The max page size for pagination.
    /// </summary>
    [JsonPropertyName("max-page-size")]
    public int? MaxPageSize { get; init; } = null;

    /// <summary>
    /// When true, nextLink in paginated responses will be relative (default: false).
    /// </summary>
    [JsonPropertyName("next-link-relative")]
    public bool? NextLinkRelative { get; init; } = false;

    [JsonConstructor]
    public PaginationOptions(int? DefaultPageSize = null, int? MaxPageSize = null, bool? NextLinkRelative = null)
    {
        if (MaxPageSize is not null)
        {
            ValidatePageSize((int)MaxPageSize);
            this.MaxPageSize = MaxPageSize == -1 ? Int32.MaxValue : (int)MaxPageSize;
            UserProvidedMaxPageSize = true;
        }
        else
        {
            this.MaxPageSize = (int)MAX_PAGE_SIZE;
        }

        if (DefaultPageSize is not null)
        {
            ValidatePageSize((int)DefaultPageSize);
            this.DefaultPageSize = DefaultPageSize == -1 ? (int)this.MaxPageSize : (int)DefaultPageSize;
            UserProvidedDefaultPageSize = true;
        }
        else
        {
            this.DefaultPageSize = (int)DEFAULT_PAGE_SIZE;
        }

        if (this.DefaultPageSize > this.MaxPageSize)
        {
            throw new DataApiBuilderException(
                message: "Pagination options invalid. The default page size cannot be greater than max page size",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
        }

        this.NextLinkRelative = NextLinkRelative ?? false;
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write default page size.
    /// property and value to the runtime config file.
    /// When user doesn't provide the default-page-size property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a default-page-size
    /// property/value specified would be interpreted by DAB as "user explicitly default-page-size."
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
    /// property/value specified would be interpreted by DAB as "user explicitly max-page-size."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(MaxPageSize))]
    public bool UserProvidedMaxPageSize { get; init; } = false;

    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize < -1 || pageSize == 0 || pageSize > Int32.MaxValue)
        {
            throw new DataApiBuilderException(
                message: "Pagination options invalid. Page size arguments cannot be 0, exceed max int value or be less than -1",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
        }
    }
}

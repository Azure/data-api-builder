// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record JwtOptions(
    string? Audience,
    string? Issuer,
    [property: JsonPropertyName("roles-path")] string? RolesPath = null,
    [property: JsonPropertyName("roles-format")] string? RolesFormat = null,
    [property: JsonPropertyName("roles-delimiter")] string? RolesDelimiter = null)
{
    public const string DEFAULT_ROLES_PATH = "roles";
    public const string DEFAULT_ROLES_FORMAT = "array";
    public const string DEFAULT_ROLES_DELIMITER = " ";
    public const string ROLES_FORMAT_ARRAY = "array";
    public const string ROLES_FORMAT_STRING = "string";
    public const string ROLES_FORMAT_DELIMITED_STRING = "delimited-string";

    public string ResolvedRolesPath => RolesPath ?? DEFAULT_ROLES_PATH;

    public string ResolvedRolesFormat => RolesFormat ?? DEFAULT_ROLES_FORMAT;

    public string ResolvedRolesDelimiter => RolesDelimiter ?? DEFAULT_ROLES_DELIMITER;
};

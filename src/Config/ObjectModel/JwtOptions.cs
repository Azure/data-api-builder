// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record JwtOptions(
    string? Audience,
    string? Issuer,
    [property: JsonPropertyName("rolesPath")] string? RolesPath = null,
    [property: JsonPropertyName("rolesFormat")] string? RolesFormat = null)
{
    public const string DEFAULT_ROLES_PATH = "roles";
    public const string DEFAULT_ROLES_FORMAT = "array";
    public const string ROLES_FORMAT_ARRAY = "array";
    public const string ROLES_FORMAT_STRING = "string";
    public const string ROLES_FORMAT_SPACE_DELIMITED = "space-delimited";
    public const string ROLES_FORMAT_COMMA_DELIMITED = "comma-delimited";

    public string ResolvedRolesPath => RolesPath ?? DEFAULT_ROLES_PATH;

    public string ResolvedRolesFormat => RolesFormat ?? DEFAULT_ROLES_FORMAT;
};

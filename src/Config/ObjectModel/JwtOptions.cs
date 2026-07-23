// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record JwtOptions
{
    [JsonPropertyName("audience")]
    public string? Audience { get; init; }

    [JsonPropertyName("issuer")]
    public string? Issuer { get; init; }

    [JsonPropertyName("rolesPath")]
    public string? RolesPath { get; init; }

    [JsonPropertyName("rolesSeparator")]
    public string? RolesSeparator { get; init; }

    [JsonPropertyName("jwksUrl")]
    public string? JwksUrl { get; init; }

    public string ResolvedRoleClaimType => string.IsNullOrWhiteSpace(RolesPath)
        ? AuthenticationOptions.ROLE_CLAIM_TYPE
        : RolesPath;

    public string? ResolvedRolesSeparator => string.IsNullOrEmpty(RolesSeparator)
        ? null
        : RolesSeparator;

    public string? ResolvedJwksUrl => string.IsNullOrWhiteSpace(JwksUrl)
        ? (string.IsNullOrWhiteSpace(Issuer) ? null : $"{Issuer.TrimEnd('/')}/.well-known/jwks.json")
        : JwksUrl;
}

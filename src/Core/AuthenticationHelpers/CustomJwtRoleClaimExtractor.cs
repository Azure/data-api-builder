// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

public static class CustomJwtRoleClaimExtractor
{
    public const string CUSTOM_JWT_ROLE_SETTINGS_PROVIDER_ERROR = "jwt.rolesPath and jwt.rolesFormat are only supported when authentication.provider is Custom.";

    public static bool IsValidRolesPath(string rolesPath)
    {
        return !string.IsNullOrWhiteSpace(rolesPath) &&
            (!rolesPath.StartsWith("$[", StringComparison.Ordinal) || IsBracketLiteralPath(rolesPath));
    }

    public static bool IsValidRolesFormat(string rolesFormat)
    {
        return rolesFormat is JwtOptions.ROLES_FORMAT_ARRAY
            or JwtOptions.ROLES_FORMAT_STRING
            or JwtOptions.ROLES_FORMAT_SPACE_DELIMITED
            or JwtOptions.ROLES_FORMAT_COMMA_DELIMITED;
    }

    public static void ConfigureCustomJwtRoleExtraction(this JwtBearerOptions options, AuthenticationOptions authOptions)
    {
        if (!authOptions.IsCustomAuthenticationProvider() || authOptions.Jwt is null)
        {
            return;
        }

        options.Events ??= new JwtBearerEvents();
        Func<TokenValidatedContext, Task> existingOnTokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            await existingOnTokenValidated(context);

            ILogger logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(CustomJwtRoleClaimExtractor));

            if (!TryGetPayloadJson(context.SecurityToken, out string? payloadJson))
            {
                logger.LogError("Unable to read JWT payload for Custom role extraction.");
                context.Fail("Unable to read JWT payload for Custom role extraction.");
                return;
            }

            if (!TryExtractRoles(
                payloadJson!,
                authOptions.Jwt.ResolvedRolesPath,
                authOptions.Jwt.ResolvedRolesFormat,
                logger,
                out IReadOnlyList<string> roles))
            {
                context.Fail("Unable to extract configured JWT roles.");
                return;
            }

            ReplaceRoleClaims(context.Principal, roles);
        };
    }

    public static bool TryExtractRoles(
        string payloadJson,
        string rolesPath,
        string rolesFormat,
        ILogger logger,
        out IReadOnlyList<string> roles)
    {
        roles = Array.Empty<string>();

        using JsonDocument payload = JsonDocument.Parse(payloadJson);
        if (!TryResolvePath(payload.RootElement, rolesPath, out JsonElement rolesElement))
        {
            logger.LogError("Configured rolesPath '{path}' was not found in JWT claims.", rolesPath);
            return false;
        }

        if (!TryParseRolesElement(rolesElement, rolesPath, rolesFormat, logger, out IEnumerable<string>? parsedRoles))
        {
            return false;
        }

        roles = NormalizeRoles(parsedRoles!);
        if (roles.Count == 0)
        {
            logger.LogWarning("JWT rolesPath '{path}' resolved successfully but produced no roles.", rolesPath);
        }

        return true;
    }

    private static bool TryResolvePath(JsonElement payload, string rolesPath, out JsonElement resolvedElement)
    {
        resolvedElement = default;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (rolesPath.StartsWith("http://", StringComparison.Ordinal) ||
            rolesPath.StartsWith("https://", StringComparison.Ordinal))
        {
            return payload.TryGetProperty(rolesPath, out resolvedElement);
        }

        if (IsBracketLiteralPath(rolesPath))
        {
            string literalKey = rolesPath[3..^2];
            return payload.TryGetProperty(literalKey, out resolvedElement);
        }

        if (rolesPath.Contains(".", StringComparison.Ordinal))
        {
            JsonElement current = payload;
            foreach (string segment in rolesPath.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(segment, out current))
                {
                    return false;
                }
            }

            resolvedElement = current;
            return true;
        }

        return payload.TryGetProperty(rolesPath, out resolvedElement);
    }

    private static bool TryParseRolesElement(
        JsonElement rolesElement,
        string rolesPath,
        string rolesFormat,
        ILogger logger,
        out IEnumerable<string>? roles)
    {
        roles = null;
        switch (rolesFormat)
        {
            case JwtOptions.ROLES_FORMAT_ARRAY:
                if (rolesElement.ValueKind != JsonValueKind.Array)
                {
                    LogUnsupportedType(logger, rolesPath, rolesElement, "JSON array of strings");
                    return false;
                }

                List<string> arrayRoles = new();
                foreach (JsonElement item in rolesElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        LogUnsupportedType(logger, rolesPath, item, "JSON array of strings");
                        return false;
                    }

                    arrayRoles.Add(item.GetString()!);
                }

                roles = arrayRoles;
                return true;

            case JwtOptions.ROLES_FORMAT_STRING:
                return TryParseStringRoles(rolesElement, rolesPath, logger, out roles, roleString => new[] { roleString });

            case JwtOptions.ROLES_FORMAT_SPACE_DELIMITED:
                return TryParseStringRoles(
                    rolesElement,
                    rolesPath,
                    logger,
                    out roles,
                    roleString => roleString.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            case JwtOptions.ROLES_FORMAT_COMMA_DELIMITED:
                return TryParseStringRoles(
                    rolesElement,
                    rolesPath,
                    logger,
                    out roles,
                    roleString => roleString.Split(',', StringSplitOptions.None));

            default:
                logger.LogError("Roles claim at '{path}' has unsupported type '{type}'. Expected {expected}.", rolesPath, rolesFormat, "array, string, space-delimited, or comma-delimited");
                return false;
        }
    }

    private static bool TryParseStringRoles(
        JsonElement rolesElement,
        string rolesPath,
        ILogger logger,
        out IEnumerable<string>? roles,
        Func<string, IEnumerable<string>> parser)
    {
        roles = null;
        if (rolesElement.ValueKind != JsonValueKind.String)
        {
            LogUnsupportedType(logger, rolesPath, rolesElement, "JSON string");
            return false;
        }

        roles = parser(rolesElement.GetString()!);
        return true;
    }

    private static List<string> NormalizeRoles(IEnumerable<string> parsedRoles)
    {
        HashSet<string> seenRoles = new(StringComparer.Ordinal);
        List<string> roles = new();

        foreach (string role in parsedRoles)
        {
            string normalizedRole = role.Trim();
            if (normalizedRole.Length > 0 && seenRoles.Add(normalizedRole))
            {
                roles.Add(normalizedRole);
            }
        }

        return roles;
    }

    private static void ReplaceRoleClaims(ClaimsPrincipal? principal, IReadOnlyList<string> roles)
    {
        if (principal is null)
        {
            return;
        }

        foreach (ClaimsIdentity identity in principal.Identities)
        {
            foreach (Claim claim in identity.FindAll(AuthenticationOptions.ROLE_CLAIM_TYPE).ToList())
            {
                identity.TryRemoveClaim(claim);
            }
        }

        ClaimsIdentity? targetIdentity = principal.Identities.FirstOrDefault(identity => identity.IsAuthenticated)
            ?? principal.Identity as ClaimsIdentity;

        if (targetIdentity is null)
        {
            return;
        }

        foreach (string role in roles)
        {
            targetIdentity.AddClaim(new Claim(AuthenticationOptions.ROLE_CLAIM_TYPE, role, ClaimValueTypes.String));
        }
    }

    private static bool TryGetPayloadJson(SecurityToken securityToken, out string? payloadJson)
    {
        payloadJson = securityToken switch
        {
            JsonWebToken jsonWebToken => Base64UrlEncoder.Decode(jsonWebToken.EncodedPayload),
            JwtSecurityToken jwtSecurityToken => jwtSecurityToken.Payload.SerializeToJson(),
            _ => null
        };

        return payloadJson is not null;
    }

    private static bool IsBracketLiteralPath(string rolesPath)
    {
        return rolesPath.Length >= 6 &&
            rolesPath.StartsWith("$['", StringComparison.Ordinal) &&
            rolesPath.EndsWith("']", StringComparison.Ordinal);
    }

    private static void LogUnsupportedType(ILogger logger, string rolesPath, JsonElement rolesElement, string expected)
    {
        logger.LogError(
            "Roles claim at '{path}' has unsupported type '{type}'. Expected {expected}.",
            rolesPath,
            GetJsonTypeName(rolesElement),
            expected);
    }

    private static string GetJsonTypeName(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => element.ValueKind.ToString().ToLowerInvariant()
        };
    }
}

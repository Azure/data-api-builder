// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

public static class CustomJwtRoleClaimExtractor
{
    public const string CUSTOM_JWT_ROLE_SETTINGS_PROVIDER_ERROR = "jwt.roles-path, jwt.roles-format, and jwt.roles-delimiter are only supported when authentication.provider is Custom.";

    public enum RoleExtractionResult
    {
        Success,
        MissingClaim,
        InvalidClaimValue
    }

    public static bool IsValidRolesPath(string rolesPath)
    {
        return IsVariableReference(rolesPath) || TryParseRolesPath(rolesPath, out _);
    }

    public static bool IsValidRolesFormat(string rolesFormat)
    {
        return rolesFormat is JwtOptions.ROLES_FORMAT_ARRAY
            or JwtOptions.ROLES_FORMAT_STRING
            or JwtOptions.ROLES_FORMAT_DELIMITED_STRING;
    }

    public static bool IsValidRolesDelimiter(string rolesDelimiter)
    {
        return !string.IsNullOrEmpty(rolesDelimiter) || IsVariableReference(rolesDelimiter);
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

            if (!context.HttpContext.Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out var requestedRole) ||
                requestedRole.Count == 0 ||
                string.IsNullOrEmpty(requestedRole[0]))
            {
                return;
            }

            if (!TryGetPayloadJson(context.SecurityToken, out string? payloadJson))
            {
                logger.LogError("Unable to read JWT payload for Custom role extraction.");
                context.Fail("Unable to read JWT payload for Custom role extraction.");
                return;
            }

            RoleExtractionResult extractionResult = ExtractRoles(
                payloadJson!,
                authOptions.Jwt.ResolvedRolesPath,
                authOptions.Jwt.ResolvedRolesFormat,
                authOptions.Jwt.ResolvedRolesDelimiter,
                logger,
                out IReadOnlyList<string> roles);

            if (extractionResult != RoleExtractionResult.Success)
            {
                LogRoleExtractionFailure(context, authOptions, requestedRole[0]!, extractionResult, logger);
                if (extractionResult == RoleExtractionResult.InvalidClaimValue)
                {
                    context.Fail("Unable to extract configured JWT roles.");
                }

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
        return ExtractRoles(payloadJson, rolesPath, rolesFormat, JwtOptions.DEFAULT_ROLES_DELIMITER, logger, out roles) == RoleExtractionResult.Success;
    }

    public static bool TryExtractRoles(
        string payloadJson,
        string rolesPath,
        string rolesFormat,
        string rolesDelimiter,
        ILogger logger,
        out IReadOnlyList<string> roles)
    {
        return ExtractRoles(payloadJson, rolesPath, rolesFormat, rolesDelimiter, logger, out roles) == RoleExtractionResult.Success;
    }

    public static RoleExtractionResult ExtractRoles(
        string payloadJson,
        string rolesPath,
        string rolesFormat,
        string rolesDelimiter,
        ILogger logger,
        out IReadOnlyList<string> roles)
    {
        roles = Array.Empty<string>();

        using JsonDocument payload = JsonDocument.Parse(payloadJson);
        if (!TryResolvePath(payload.RootElement, rolesPath, out JsonElement rolesElement))
        {
            logger.LogError("Configured roles-path '{path}' was not found in JWT claims.", rolesPath);
            return RoleExtractionResult.MissingClaim;
        }

        if (!TryParseRolesElement(rolesElement, rolesPath, rolesFormat, rolesDelimiter, logger, out IEnumerable<string>? parsedRoles))
        {
            return RoleExtractionResult.InvalidClaimValue;
        }

        roles = NormalizeRoles(parsedRoles!);
        if (roles.Count == 0)
        {
            logger.LogWarning("JWT roles-path '{path}' resolved successfully but produced no roles.", rolesPath);
        }

        return RoleExtractionResult.Success;
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

        if (!TryParseRolesPath(rolesPath, out IReadOnlyList<string>? pathSegments))
        {
            return false;
        }

        JsonElement current = payload;
        foreach (string segment in pathSegments!)
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

    private static bool TryParseRolesElement(
        JsonElement rolesElement,
        string rolesPath,
        string rolesFormat,
        string rolesDelimiter,
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

            case JwtOptions.ROLES_FORMAT_DELIMITED_STRING:
                return TryParseStringRoles(
                    rolesElement,
                    rolesPath,
                    logger,
                    out roles,
                    roleString => roleString.Split(rolesDelimiter, StringSplitOptions.None));

            default:
                logger.LogError("Roles claim at '{path}' has unsupported roles-format value '{rolesFormat}'. Expected {expected}.", rolesPath, rolesFormat, "array, string, or delimited-string");
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

    private static void LogRoleExtractionFailure(
        TokenValidatedContext context,
        AuthenticationOptions authOptions,
        string requestedRole,
        RoleExtractionResult extractionResult,
        ILogger logger)
    {
        logger.LogError(
            "{correlationId} Custom JWT role extraction failed. Provider: {provider}. roles-path: {rolesPath}. roles-format: {rolesFormat}. Requested role: {requestedRole}. Reason: {reason}.",
            HttpContextExtensions.GetLoggerCorrelationId(context.HttpContext),
            authOptions.Provider,
            authOptions.Jwt!.ResolvedRolesPath,
            authOptions.Jwt.ResolvedRolesFormat,
            requestedRole,
            extractionResult);
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

    private static bool TryParseRolesPath(string rolesPath, out IReadOnlyList<string>? pathSegments)
    {
        pathSegments = null;
        if (string.IsNullOrWhiteSpace(rolesPath))
        {
            return false;
        }

        if (rolesPath.StartsWith("http://", StringComparison.Ordinal) ||
            rolesPath.StartsWith("https://", StringComparison.Ordinal))
        {
            pathSegments = new[] { rolesPath };
            return true;
        }

        int index = 0;
        List<string> segments = new();
        while (index < rolesPath.Length)
        {
            if (rolesPath[index] == '.')
            {
                return false;
            }

            if (rolesPath[index] == '[')
            {
                if (index + 3 >= rolesPath.Length || rolesPath[index + 1] != '\'')
                {
                    return false;
                }

                int literalEnd = rolesPath.IndexOf("']", index + 2, StringComparison.Ordinal);
                if (literalEnd < 0 || literalEnd == index + 2)
                {
                    return false;
                }

                segments.Add(rolesPath[(index + 2)..literalEnd]);
                index = literalEnd + 2;
            }
            else
            {
                int segmentStart = index;
                while (index < rolesPath.Length && rolesPath[index] != '.' && rolesPath[index] != '[')
                {
                    index++;
                }

                if (segmentStart == index)
                {
                    return false;
                }

                segments.Add(rolesPath[segmentStart..index]);
            }

            if (index == rolesPath.Length)
            {
                pathSegments = segments;
                return true;
            }

            if (rolesPath[index] == '.')
            {
                index++;
                if (index == rolesPath.Length)
                {
                    return false;
                }
            }
            else if (rolesPath[index] != '[')
            {
                return false;
            }
        }

        pathSegments = segments;
        return segments.Count > 0;
    }

    private static bool IsVariableReference(string value)
    {
        return (value.StartsWith("@env('", StringComparison.Ordinal) && value.EndsWith("')", StringComparison.Ordinal)) ||
            (value.StartsWith("@akv('", StringComparison.Ordinal) && value.EndsWith("')", StringComparison.Ordinal));
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

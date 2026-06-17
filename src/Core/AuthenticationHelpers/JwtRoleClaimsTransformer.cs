// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Claims;
using System.Text.Json;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

public static class JwtRoleClaimsTransformer
{
    public static void NormalizeRoleClaims(
        ClaimsPrincipal principal,
        string sourceRoleClaimType,
        string? separator)
    {
        foreach (ClaimsIdentity identity in principal.Identities)
        {
            if (!identity.IsAuthenticated)
            {
                continue;
            }

            List<Claim> sourceClaims = identity.Claims
                .Where(c => c.Type.Equals(sourceRoleClaimType, StringComparison.Ordinal))
                .ToList();

            if (sourceClaims.Count == 0)
            {
                continue;
            }

            HashSet<string> normalizedValues = new(StringComparer.OrdinalIgnoreCase);

            foreach (Claim claim in sourceClaims)
            {
                foreach (string expandedValue in ExpandClaimValues(claim.Value, separator))
                {
                    if (!string.IsNullOrWhiteSpace(expandedValue))
                    {
                        normalizedValues.Add(expandedValue.Trim());
                    }
                }
            }

            foreach (string normalizedValue in normalizedValues)
            {
                bool exactClaimAlreadyExists = identity.Claims.Any(c =>
                    c.Type.Equals(sourceRoleClaimType, StringComparison.Ordinal) &&
                    c.Value.Equals(normalizedValue, StringComparison.OrdinalIgnoreCase));

                if (!exactClaimAlreadyExists)
                {
                    identity.AddClaim(new Claim(sourceRoleClaimType, normalizedValue, ClaimValueTypes.String));
                }
            }
        }
    }

    private static IEnumerable<string> ExpandClaimValues(string rawValue, string? separator)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            yield break;
        }

        string trimmed = rawValue.Trim();

        // 1. JSON array support
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            List<string>? values;
            try
            {
                values = JsonSerializer.Deserialize<List<string>>(trimmed);
            }
            catch (JsonException)
            {
                values = null;
            }

            if (values is not null)
            {
                foreach (string value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value.Trim();
                    }
                }

                yield break;
            }
        }

        // 2. Configurable separated string support
        if (!string.IsNullOrEmpty(separator))
        {
            string[] splitValues;

            if (separator.Length == 1)
            {
                splitValues = trimmed.Split(
                    separator[0],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else
            {
                splitValues = trimmed.Split(
                    new[] { separator },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            foreach (string value in splitValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }

            yield break;
        }

        // 3. Single scalar fallback
        yield return trimmed;
    }
}

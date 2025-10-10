// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Mcp.Utils
{
    /// <summary>
    /// Helper class for MCP tool authorization operations.
    /// </summary>
    public static class McpAuthorizationHelper
    {
        /// <summary>
        /// Validates if the current request has a valid role context.
        /// </summary>
        public static bool ValidateRoleContext(
            HttpContext? httpContext,
            IAuthorizationResolver authResolver,
            out string error)
        {
            error = string.Empty;

            if (httpContext is null || !authResolver.IsValidRoleContext(httpContext))
            {
                error = "Unable to resolve a valid role context";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to resolve an authorized role for the given entity and operation.
        /// </summary>
        public static bool TryResolveAuthorizedRole(
            HttpContext httpContext,
            IAuthorizationResolver authorizationResolver,
            string entityName,
            EntityActionOperation operation,
            out string? effectiveRole,
            out string error)
        {
            effectiveRole = null;
            error = string.Empty;

            string roleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();

            if (string.IsNullOrWhiteSpace(roleHeader))
            {
                error = "Client role header is missing or empty.";
                return false;
            }

            string[] roles = roleHeader
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (roles.Length == 0)
            {
                error = "Client role header is missing or empty.";
                return false;
            }

            foreach (string role in roles)
            {
                bool allowed = authorizationResolver.AreRoleAndOperationDefinedForEntity(
                    entityName, role, operation);

                if (allowed)
                {
                    effectiveRole = role;
                    return true;
                }
            }

            error = $"You do not have permission to perform {operation} operation for this entity.";
            return false;
        }
    }
}

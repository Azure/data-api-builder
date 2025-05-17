// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.Authorization;

/// <summary>
/// Custom authorization implementation of HotChocolate IAuthorizationHandler interface.
/// The changes in this custom handler enable fetching the ClientRoleHeader value defined within requests (value of X-MS-API-ROLE) HTTP Header.
/// Then, using that value to check the header value against the authenticated ClientPrincipal roles.
/// </summary>
public class GraphQLAuthorizationHandler : IAuthorizationHandler
{
    /// <summary>
    /// Authorize access to field based on contents of @authorize directive.
    /// Validates that the requestor is authenticated, and that the
    /// clientRoleHeader is present.
    /// Role membership is checked
    /// and/or (authorize directive may define policy, roles, or both)
    /// An authorization policy is evaluated, if present.
    /// </summary>
    /// <param name="context">The current middleware context.</param>
    /// <param name="directive">The authorization directive.</param>
    /// <param name="cancellationToken">The cancellation token - not used here.</param>
    /// <returns>
    /// Returns a value indicating if the current session is authorized to
    /// access the resolver data.
    /// </returns>
    public ValueTask<AuthorizeResult> AuthorizeAsync(
        IMiddlewareContext context,
        AuthorizeDirective directive,
        CancellationToken cancellationToken = default)
    {
        if (!IsUserAuthenticated(context.ContextData))
        {
            return new ValueTask<AuthorizeResult>(AuthorizeResult.NotAuthenticated);
        }

        // Schemas defining authorization policies are not supported, even when roles are defined appropriately.
        // Requests will be short circuited and rejected (authorization forbidden).
        if (TryGetApiRoleHeader(context.ContextData, out string? clientRole) && IsInHeaderDesignatedRole(clientRole, directive.Roles))
        {
            if (!string.IsNullOrEmpty(directive.Policy))
            {
                return new ValueTask<AuthorizeResult>(AuthorizeResult.NotAllowed);
            }

            return new ValueTask<AuthorizeResult>(AuthorizeResult.Allowed);
        }

        return new ValueTask<AuthorizeResult>(AuthorizeResult.NotAllowed);
    }

    /// <summary>
    /// Authorize access to field based on contents of @authorize directive.
    /// Validates that the requestor is authenticated, and that the
    /// clientRoleHeader is present.
    /// Role membership is checked
    /// and/or (authorize directive may define policy, roles, or both)
    /// an authorization policy is evaluated, if present.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <param name="directives">The list of authorize directives.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authorize result.</returns>
    public ValueTask<AuthorizeResult> AuthorizeAsync(
        AuthorizationContext context,
        IReadOnlyList<AuthorizeDirective> directives,
        CancellationToken cancellationToken = default)
    {
        if (!IsUserAuthenticated(context.ContextData))
        {
            return new ValueTask<AuthorizeResult>(AuthorizeResult.NotAuthenticated);
        }

        foreach (AuthorizeDirective directive in directives)
        {
            // Schemas defining authorization policies are not supported, even when roles are defined appropriately.
            // Requests will be short circuited and rejected (authorization forbidden).
            if (TryGetApiRoleHeader(context.ContextData, out string? clientRole) && IsInHeaderDesignatedRole(clientRole, directive.Roles))
            {
                if (!string.IsNullOrEmpty(directive.Policy))
                {
                    return new ValueTask<AuthorizeResult>(AuthorizeResult.NotAllowed);
                }

                // directive is satisfied, continue to next directive.
                continue;
            }

            return new ValueTask<AuthorizeResult>(AuthorizeResult.NotAllowed);
        }

        return new ValueTask<AuthorizeResult>(AuthorizeResult.Allowed);
    }

    /// <summary>
    /// Get the value of the CLIENT_ROLE_HEADER HTTP Header from the HttpContext.
    /// HttpContext will be present in IMiddlewareContext.ContextData
    /// when HotChocolate is configured to use HttpRequestInterceptor
    /// </summary>
    /// <param name="contextData">HotChocolate Middleware Context data.</param>
    /// <param name="clientRole">Value of the client role header.</param>
    /// <seealso cref="https://chillicream.com/docs/hotchocolate/v12/server/interceptors#ihttprequestinterceptor"/>
    /// <returns>True, if clientRoleHeader is resolved and clientRole value
    /// False, if clientRoleHeader is not resolved, null clientRole value</returns>
    private static bool TryGetApiRoleHeader(IDictionary<string, object?> contextData, [NotNullWhen(true)] out string? clientRole)
    {
        if (contextData.TryGetValue(nameof(HttpContext), out object? value))
        {
            if (value is not null)
            {
                HttpContext httpContext = (HttpContext)value;
                if (httpContext.Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out StringValues clientRoleHeader))
                {
                    clientRole = clientRoleHeader.ToString();
                    return true;
                }
            }
        }

        clientRole = null;
        return false;
    }

    /// <summary>
    /// Checks the pre-validated clientRoleHeader value against the roles listed in @authorize directive's roles.
    /// The runtime's GraphQLSchemaBuilder will not add an @authorize directive without any roles defined,
    /// however, since the Roles property of HotChocolate's AuthorizeDirective object is nullable,
    /// handle the possible null gracefully.
    /// </summary>
    /// <param name="clientRoleHeader">Role defined in request HTTP Header, X-MS-API-ROLE</param>
    /// <param name="roles">Roles defined on the @authorize directive. Case insensitive.</param>
    /// <returns>True when the authenticated user's explicitly defined role is present in the authorize directive role list. Otherwise, false.</returns>
    private static bool IsInHeaderDesignatedRole(string clientRoleHeader, IReadOnlyList<string>? roles)
    {
        if (roles is null || roles.Count == 0)
        {
            return false;
        }

        if (roles.Any(role => role.Equals(clientRoleHeader, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the ClaimsPrincipal in the HotChocolate IMiddlewareContext.ContextData is authenticated.
    /// To be authenticated, at least one ClaimsIdentity in ClaimsPrincipal.Identities must be authenticated.
    /// </summary>
    private static bool IsUserAuthenticated(IDictionary<string, object?> contextData)
    {
        if (contextData.TryGetValue(nameof(ClaimsPrincipal), out object? claimsPrincipalContextObject)
            && claimsPrincipalContextObject is ClaimsPrincipal principal
            && principal.Identities.Any(claimsIdentity => claimsIdentity.IsAuthenticated))
        {
            return true;
        }

        return false;
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HotChocolate.AspNetCore.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Authorization;

/// <summary>
/// Custom authorization implementation of HotChocolate IAuthorizationHandler interface.
/// https://github.com/ChilliCream/hotchocolate/blob/main/src/HotChocolate/AspNetCore/src/AspNetCore.Authorization/IAuthorizationHandler.cs
/// Method implementation is duplicate of HotChocolate DefaultAuthorizationHandler:
/// https://github.com/ChilliCream/hotchocolate/blob/main/src/HotChocolate/AspNetCore/src/AspNetCore.Authorization/DefaultAuthorizationHandler.cs
/// The changes in this custom handler enable fetching the ClientRoleHeader value defined within requests (value of X-MS-API-ROLE) HTTP Header.
/// Then, using that value to check the header value against the authenticated ClientPrincipal roles.
/// </summary>
public class GraphQLAuthorizationHandler : HotChocolate.AspNetCore.Authorization.IAuthorizationHandler
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
    /// <returns>
    /// Returns a value indicating if the current session is authorized to
    /// access the resolver data.
    /// </returns>
    public async ValueTask<AuthorizeResult> AuthorizeAsync(
        IMiddlewareContext context,
        AuthorizeDirective directive)
    {
        if (!TryGetAuthenticatedPrincipal(context, out ClaimsPrincipal? principal))
        {
            return AuthorizeResult.NotAuthenticated;
        }

        if (!TryGetApiRoleHeader(context, out string? clientRole))
        {
            return AuthorizeResult.NotAllowed;
        }

        if (IsInHeaderDesignatedRole(clientRole!, directive.Roles))
        {
            if (NeedsPolicyValidation(directive))
            {
                return await AuthorizeWithPolicyAsync(
                        context, directive, principal!)
                    .ConfigureAwait(false);
            }

            return AuthorizeResult.Allowed;
        }

        return AuthorizeResult.NotAllowed;
    }

    /// <summary>
    /// Get the value of the CLIENT_ROLE_HEADER HTTP Header from the HttpContext.
    /// HttpContext will be present in IMiddlewareContext.ContextData
    /// when HotChocolate is configured to use HttpRequestInterceptor
    /// </summary>
    /// <param name="context">HotChocolate Middleware Context</param>
    /// <param name="clientRole">Value of the client role header.</param>
    /// <seealso cref="https://chillicream.com/docs/hotchocolate/server/interceptors/#ihttprequestinterceptor"/>
    /// <returns>True, if clientRoleHeader is resolved and clientRole value
    /// False, if clientRoleHeader is not resolved, null clientRole value</returns>
    private static bool TryGetApiRoleHeader(IMiddlewareContext context, [NotNullWhen(true)] out string? clientRole)
    {
        if (context.ContextData.TryGetValue(nameof(HttpContext), out object? value))
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
    /// Checks the pre-validated clientRoleHeader value
    /// against the roles listed in @authorize directive's roles.
    /// The runtime's GraphQLSchemaBuilder will not add an @authorize directive without any roles defined,
    /// however, since the Roles property of HotChocolate's AuthorizeDirective object is nullable,
    /// handle the possible null gracefully.
    /// </summary>
    /// <param name="clientRoleHeader">Role defined in request HTTP Header, X-MS-API-ROLE</param>
    /// <param name="roles">Roles defined on the @authorize directive.</param>
    /// <returns>True/False</returns>
    private static bool IsInHeaderDesignatedRole(
        string clientRoleHeader,
        IReadOnlyList<string>? roles)
    {
        if (roles is null || roles.Count == 0)
        {
            return false;
        }

        if (roles.Any(role => role.Equals(clientRoleHeader, StringComparison.InvariantCultureIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// This method is used verbatim from HotChocolate DefaultAuthorizationHandler.
    /// Retrieves the authenticated ClaimsPrincipal.
    /// To be authenticted, at least one ClaimsIdentity in ClaimsPrincipal.Identities
    /// must be authenticated.
    /// </summary>
    /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/main/src/HotChocolate/AspNetCore/src/AspNetCore.Authorization/DefaultAuthorizationHandler.cs"/>
    private static bool TryGetAuthenticatedPrincipal(
        IMiddlewareContext context,
        [NotNullWhen(true)] out ClaimsPrincipal? principal)
    {
        if (context.ContextData.TryGetValue(nameof(ClaimsPrincipal), out object? o)
            && o is ClaimsPrincipal p
            && p.Identities.Any(t => t.IsAuthenticated))
        {
            principal = p;
            return true;
        }

        principal = null;
        return false;
    }

    /// <summary>
    /// This method is used verbatim from HotChocolate DefaultAuthorizationHandler.
    /// Checks whether a policy is present on the authorize directive
    /// e.g. @authorize(policy: "SalesDepartment")
    /// </summary>
    /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/main/src/HotChocolate/AspNetCore/src/AspNetCore.Authorization/DefaultAuthorizationHandler.cs"/>
    private static bool NeedsPolicyValidation(AuthorizeDirective directive)
        => directive.Roles == null
           || directive.Roles.Count == 0
           || !string.IsNullOrEmpty(directive.Policy);

    /// <summary>
    /// This method is used verbatim from HotChocolate DefaultAuthorizationHandler.
    /// Authorizes the user based on a policy, if present on the authorize directive
    /// e.g. @authorize(policy: "SalesDepartment")
    /// </summary>
    /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/main/src/HotChocolate/AspNetCore/src/AspNetCore.Authorization/DefaultAuthorizationHandler.cs"/>
    /// <returns></returns>
    private static async Task<AuthorizeResult> AuthorizeWithPolicyAsync(
        IMiddlewareContext context,
        AuthorizeDirective directive,
        ClaimsPrincipal principal)
    {
        IServiceProvider services = context.Service<IServiceProvider>();
        IAuthorizationService? authorizeService =
            services.GetService<IAuthorizationService>();
        IAuthorizationPolicyProvider? policyProvider =
            services.GetService<IAuthorizationPolicyProvider>();

        if (authorizeService == null || policyProvider == null)
        {
            // authorization service is not configured so the user is
            // authorized with the previous checks.
            return string.IsNullOrWhiteSpace(directive.Policy)
                ? AuthorizeResult.Allowed
                : AuthorizeResult.NotAllowed;
        }

        AuthorizationPolicy? policy = null;

        if ((directive.Roles is null || directive.Roles.Count == 0)
            && string.IsNullOrWhiteSpace(directive.Policy))
        {
            policy = await policyProvider.GetDefaultPolicyAsync()
                .ConfigureAwait(false);

            if (policy == null)
            {
                return AuthorizeResult.NoDefaultPolicy;
            }
        }
        else if (!string.IsNullOrWhiteSpace(directive.Policy))
        {
            policy = await policyProvider.GetPolicyAsync(directive.Policy)
                .ConfigureAwait(false);

            if (policy == null)
            {
                return AuthorizeResult.PolicyNotFound;
            }
        }

        if (policy is not null)
        {
            AuthorizationResult result =
                await authorizeService.AuthorizeAsync(principal, context, policy)
                    .ConfigureAwait(false);
            return result.Succeeded ? AuthorizeResult.Allowed : AuthorizeResult.NotAllowed;
        }

        return AuthorizeResult.NotAllowed;
    }
}

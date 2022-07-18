using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using HotChocolate.AspNetCore.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Azure.DataGateway.Service.Authorization;

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
    /// Authorize current directive using Microsoft.AspNetCore.Authorization.
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
    /// To get HttpContext, enable HttpRequestInterceptor when configuring HotChocolate
    /// https://chillicream.com/docs/hotchocolate/server/interceptors/#ihttprequestinterceptor
    /// </summary>
    /// <param name="context"></param>
    /// <param name="clientRole"></param>
    /// <returns></returns>
    private static bool TryGetApiRoleHeader(IMiddlewareContext context, [NotNullWhen(true)] out string? clientRole)
    {
        if (context.ContextData.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out object? o)
            && o is StringValues clientRoleHeader)
        {
            clientRole = clientRoleHeader.ToString();
            return true;
        }

        clientRole = null;
        return false;
    }

    /// <summary>
    /// Checks the pre-validated clientRoleHeader value
    /// against the roles listed in @authorize directive's roles.
    /// </summary>
    /// <param name="clientRoleHeader">Role defined in request HTTP Header, X-MS-API-ROLE</param>
    /// <param name="roles">Roles defined on the @authorize directive.</param>
    /// <returns>True/False</returns>
    private static bool IsInHeaderDesignatedRole(
        string clientRoleHeader,
        IReadOnlyList<string>? roles)
    {
        if (roles == null || roles.Count == 0)
        {
            return true;
        }

        if (roles.Any( role => role.Equals(clientRoleHeader, StringComparison.OrdinalIgnoreCase) ))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    private static bool IsInAnyRole(
        IPrincipal principal,
        IReadOnlyList<string>? roles)
    {
        if (roles == null || roles.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < roles.Count; i++)
        {
            if (principal.IsInRole(roles[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    private static bool NeedsPolicyValidation(AuthorizeDirective directive)
        => directive.Roles == null
           || directive.Roles.Count == 0
           || !string.IsNullOrEmpty(directive.Policy);

    /// <inheritdoc />
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

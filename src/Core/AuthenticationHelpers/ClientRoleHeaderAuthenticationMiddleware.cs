// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Claims;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers.AuthenticationSimulator;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AuthenticationOptions = Azure.DataApiBuilder.Config.ObjectModel.AuthenticationOptions;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// This middleware validates JWT tokens when JWT Auth is configured
/// and an Authorization HTTP header is present with a token.
/// This is required since Asp.Net Core UseAuthentication() does not make
/// AuthZ decisions nor does it terminate requests.
/// https://github.com/aspnet/Security/issues/1613#issuecomment-358843214
/// </summary>
public class ClientRoleHeaderAuthenticationMiddleware
{
    private const string ANONYOUMOUS_ROLE = "Anonymous";
    private const string AUTHENTICATED_ROLE = "Authenticated";
    private readonly RequestDelegate _nextMiddleware;
    private ILogger<ClientRoleHeaderAuthenticationMiddleware> _logger;
    private RuntimeConfigProvider _runtimeConfigProvider;

    // Identity provider used for identities added to the ClaimsPrincipal object for the current user by DAB.
    private const string INTERNAL_DAB_IDENTITY_PROVIDER = "DAB-VERIFIED";

    public ClientRoleHeaderAuthenticationMiddleware(RequestDelegate next,
        ILogger<ClientRoleHeaderAuthenticationMiddleware> logger,
        RuntimeConfigProvider runtimeConfigProvider)
    {
        _nextMiddleware = next;
        _logger = logger;
        _runtimeConfigProvider = runtimeConfigProvider;
    }

    /// <summary>
    /// Middleware to authenticate requests where the method
    /// AuthenticateAsync() calls HandleAuthenticateAsync() in one of:
    /// - EasyAuthAuthenticationHandler
    /// - JwtBearerHandler (internal Asp.Net Core class)
    /// A successful result contains validated token data that is
    /// used to retrieve the `identity` from within the Principal in the HttpContext for use
    /// in downstream middleware.
    /// Based on the AuthenticateResult, the clientRoleHeader will be
    /// validated or set.
    /// AuthenticateResult: None -> Anonymous
    /// AuthenticateResult: Succeeded -> Authenticated/Honor client role header
    /// </summary>
    /// <param name="httpContext">Request metadata</param>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        // Determine the authentication scheme to use based on dab-config.json.
        // Compatible with both ConfigureAuthentication and ConfigureAuthenticationV2 in startup.cs.
        // This means that this code is resilient to whether or not the default authentication scheme is set in startup.
        string scheme = EasyAuthAuthenticationDefaults.SWAAUTHSCHEME;
        if (!_runtimeConfigProvider.IsLateConfigured)
        {
            AuthenticationOptions? dabAuthNOptions = _runtimeConfigProvider.GetConfig().Runtime?.Host?.Authentication;
            scheme = ResolveConfiguredAuthNScheme(dabAuthNOptions?.Provider);
        }

        // authNResult will be one of:
        // 1. Succeeded - Authenticated
        // 2. Failure - Token issue
        // 3. None - No token provided, no auth result.
        AuthenticateResult authNResult = await httpContext.AuthenticateAsync(scheme);

        // Reject and terminate the request when an invalid token is provided
        // Write challenge response metadata (HTTP 401 Unauthorized response code
        // and www-authenticate headers) to the HTTP Context via JwtBearerHandler code
        // https://github.com/dotnet/aspnetcore/blob/3fe12b935c03138f76364dc877a7e069e254b5b2/src/Security/Authentication/JwtBearer/src/JwtBearerHandler.cs#L217
        if (authNResult.Failure is not null)
        {
            await httpContext.ChallengeAsync(scheme);
            return;
        }

        // Manually set the httpContext.User to the Principal from the AuthenticateResult
        // when we exclude setting a default authentication scheme in Startup.cs AddAuthentication().
        // https://learn.microsoft.com/aspnet/core/security/authorization/limitingidentitybyscheme
        if (authNResult.Succeeded)
        {
            httpContext.User = authNResult.Principal;
        }

        string clientDefinedRole = ANONYOUMOUS_ROLE;

        // A request can be authenticated in 2 cases:
        // 1. When the request has a valid jwt/easyauth token,
        // 2. When using simulator authentication in development mode.
        bool isAuthenticatedRequest = httpContext.User.Identity?.IsAuthenticated ?? false;

        if (isAuthenticatedRequest)
        {
            clientDefinedRole = AUTHENTICATED_ROLE;
        }

        // Attempt to inject CLIENT_ROLE_HEADER:clientDefinedRole into the httpContext
        // to accommodate client requests that do not include such header.
        // otherwise honor existing CLIENT_ROLE_HEADER:Value
        if (!httpContext.Request.Headers.TryAdd(AuthorizationResolver.CLIENT_ROLE_HEADER, clientDefinedRole))
        {
            // Honor the client role header value already included
            // in an authenticated requests.
            if (isAuthenticatedRequest)
            {
                clientDefinedRole = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
            }
            else
            {
                // Override existing client role header value for anonymous requests.
                httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]
                    = clientDefinedRole;
            }
        }

        // Log the request's authenticated status (anonymous/authenticated) and user role,
        // only in the non-hosted scenario.
        if (!_runtimeConfigProvider.IsLateConfigured)
        {
            string correlationId = HttpContextExtensions.GetLoggerCorrelationId(httpContext);
            string requestAuthStatus = isAuthenticatedRequest ? AuthorizationType.Authenticated.ToString() : AuthorizationType.Anonymous.ToString();
            _logger.LogDebug(
                message: "{correlationId} AuthN state: {requestAuthStatus}. Role: {clientDefinedRole}. Scheme: {scheme}",
                correlationId,
                requestAuthStatus,
                clientDefinedRole,
                scheme);
        }

        // When the user is not in the clientDefinedRole and the client role header
        // is resolved to a system role (anonymous, authenticated), add the matching system
        // role name as a role claim to the ClaimsIdentity.
        if (!httpContext.User.IsInRole(clientDefinedRole) && IsSystemRole(clientDefinedRole))
        {
            Claim claim = new(AuthenticationOptions.ROLE_CLAIM_TYPE, clientDefinedRole, ClaimValueTypes.String);
            string authenticationType = isAuthenticatedRequest ? INTERNAL_DAB_IDENTITY_PROVIDER : string.Empty;

            // Add identity with the same value of IsAuthenticated flag as the original identity.
            ClaimsIdentity identity = new(
                authenticationType: authenticationType,
                nameType: AuthenticationOptions.NAME_CLAIM_TYPE,
                roleType: AuthenticationOptions.ROLE_CLAIM_TYPE);
            identity.AddClaim(claim);
            httpContext.User.AddIdentity(identity);
        }

        await _nextMiddleware(httpContext);
    }

    /// <summary>
    /// Determines whether the given role name matches one of the reserved system role names:
    /// 1. Anonymous
    /// 2. Authenticated
    /// </summary>
    /// <param name="roleName">Name of role to evaluate</param>
    /// <returns>True if roleName is a system role.</returns>
    public static bool IsSystemRole(string roleName)
    {
        return roleName.Equals(AuthorizationType.Authenticated.ToString(), StringComparison.OrdinalIgnoreCase) ||
                roleName.Equals(AuthorizationType.Anonymous.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Uses the dab-config.json's Authentication provider name to resolve the
    /// authentication scheme to use with httpContext.AuthenticateAsync(scheme).
    /// </summary>
    /// <param name="configuredProviderName">Dab config defined authentication provider name.</param>
    /// <returns>Authentication Scheme</returns>
    private static string ResolveConfiguredAuthNScheme(string? configuredProviderName)
    {
        if (string.IsNullOrWhiteSpace(configuredProviderName)
            || string.Equals(configuredProviderName, SupportedAuthNProviders.STATIC_WEB_APPS, StringComparison.OrdinalIgnoreCase))
        {
            return EasyAuthAuthenticationDefaults.SWAAUTHSCHEME;
        }
        else if (string.Equals(configuredProviderName, SupportedAuthNProviders.APP_SERVICE, StringComparison.OrdinalIgnoreCase))
        {
            return EasyAuthAuthenticationDefaults.APPSERVICEAUTHSCHEME;
        }
        else if (string.Equals(configuredProviderName, SupportedAuthNProviders.SIMULATOR, StringComparison.OrdinalIgnoreCase))
        {
            return SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME;
        }
        else if (string.Equals(configuredProviderName, SupportedAuthNProviders.AZURE_AD, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configuredProviderName, SupportedAuthNProviders.ENTRA_ID, StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }
        else
        {
            // Changing this value is a breaking change because non-out of box
            // authentication provider names supplied in dab-config.json indicate
            // that JWT bearer authentication should be used.
            return GenericOAuthDefaults.AUTHENTICATIONSCHEME;
        }
    }
}

// Extension method used to add the middleware to the HTTP request pipeline.
public static class ClientRoleHeaderMiddlewareExtensions
{
    public static IApplicationBuilder UseClientRoleHeaderAuthenticationMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ClientRoleHeaderAuthenticationMiddleware>();
    }
}

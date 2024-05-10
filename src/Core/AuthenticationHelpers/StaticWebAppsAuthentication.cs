// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// Helper class which parses EasyAuth's injected headers into a ClaimsIdentity object.
/// This class provides helper methods for StaticWebApps' Authentication feature: EasyAuth.
/// </summary>
public class StaticWebAppsAuthentication
{
    public const string USER_ID_CLAIM = "userId";
    public const string USER_DETAILS_CLAIM = "userDetails";

    /// <summary>
    /// Link for reference of how StaticWebAppsClientPrincipal is defined
    /// https://docs.microsoft.com/azure/static-web-apps/user-information?tabs=csharp#client-principal-data
    /// </summary>
    public class StaticWebAppsClientPrincipal
    {
        public string? IdentityProvider { get; set; }
        public string? UserId { get; set; }
        public string? UserDetails { get; set; }
        public IEnumerable<string>? UserRoles { get; set; }
    }

    /// <summary>
    /// Base64 decodes and deserializes the x-ms-client-principal payload containing
    /// SWA token metadata.
    /// Writes SWA token metadata (roles and token claims) to .NET ClaimsIdentity object.
    /// </summary>
    /// <param name="context"></param>
    /// <returns>ClaimsIdentity containing authentication metadata.</returns>
    public static ClaimsIdentity? Parse(HttpContext context, ILogger logger)
    {
        ClaimsIdentity? identity = null;
        StaticWebAppsClientPrincipal principal = new();
        try
        {
            if (context.Request.Headers.TryGetValue(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, out StringValues headerPayload) && headerPayload.Count == 1)
            {
                string data = headerPayload.ToString();
                byte[] decoded = Convert.FromBase64String(data);
                string json = Encoding.UTF8.GetString(decoded);
                principal = JsonSerializer.Deserialize<StaticWebAppsClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }

            // Null UserRoles collection indicates that SWA authentication failed
            // because all requests will at least have the 'anonymous role'
            if (principal is null || principal.UserRoles is null || !principal.UserRoles.Any())
            {
                return identity;
            }

            identity = new(authenticationType: principal.IdentityProvider, nameType: USER_ID_CLAIM, roleType: AuthenticationOptions.ROLE_CLAIM_TYPE);

            if (!string.IsNullOrWhiteSpace(principal.UserId))
            {
                identity.AddClaim(new Claim(USER_ID_CLAIM, principal.UserId));
            }

            if (!string.IsNullOrWhiteSpace(principal.UserDetails))
            {
                identity.AddClaim(new Claim(USER_DETAILS_CLAIM, principal.UserDetails));
            }

            // output identity.Claims
            // [0] { Type = "roles", Value = "roleName" }
            identity.AddClaims(principal.UserRoles.Select(roleName => new Claim(AuthenticationOptions.ROLE_CLAIM_TYPE, roleName)));

            return identity;
        }
        catch (Exception error) when (
            error is JsonException ||
            error is ArgumentNullException ||
            error is NotSupportedException ||
            error is InvalidOperationException)
        {
            // Log any SWA token processing failures in the logger.
            // Does not raise or rethrow a DataApiBuilder exception because
            // the authentication handler caller will return a 401 unauthorized
            // response to the client.
            logger.LogError(
                exception: error,
                message: "{correlationId} Failure processing the StaticWebApps EasyAuth header due to:\n{errorMessage}",
                HttpContextExtensions.GetLoggerCorrelationId(context),
                error.Message);
        }

        return identity;
    }
}

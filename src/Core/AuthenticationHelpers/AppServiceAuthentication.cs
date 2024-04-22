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
/// This class provides helper methods for AppService's Authentication feature: EasyAuth.
/// </summary>
public static class AppServiceAuthentication
{
    /// <summary>
    /// Representation of authenticated user principal Http header
    /// injected by EasyAuth
    /// </summary>
    public class AppServiceClientPrincipal
    {
        /// <summary>
        /// The type of authentication used, unauthenticated request when null.
        /// </summary>
        public string Auth_typ { get; set; } = null!;
        /// <summary>
        /// The Claim.Type used when obtaining the value of <see cref="ClaimsIdentity.Name"/>.
        /// </summary>
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsidentity.nameclaimtype?view=net-6.0"/>
        public string? Name_typ { get; set; }
        /// <summary>
        /// The Claim.Type used when performing logic for <see cref="ClaimsPrincipal.IsInRole"/>.
        /// </summary>
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsidentity.roleclaimtype?view=net-6.0"/>
        public string? Role_typ { get; set; }
        /// <summary>
        /// Collection of claims optionally present.
        /// </summary>
        public IEnumerable<AppServiceClaim>? Claims { get; set; }
    }

    /// <summary>
    /// Representation of authenticated user principal claims
    /// injected by EasyAuth
    /// </summary>
    public class AppServiceClaim
    {
        public string? Typ { get; set; }
        public string? Val { get; set; }
    }

    /// <summary>
    /// Create ClaimsIdentity object from EasyAuth injected x-ms-client-principal injected header,
    /// the value is a base64 encoded custom JWT injected by EasyAuth as a result of validating a bearer token.
    /// If present, copies all AppService token claims to .NET ClaimsIdentity object.
    /// </summary>
    /// <param name="context">Request's Http Context</param>
    /// <returns>
    /// Success: Hydrated ClaimsIdentity object.
    /// Failure: null, which indicates parsing failed, and can be interpreted
    /// as an authentication failure.
    /// </returns>
    public static ClaimsIdentity? Parse(HttpContext context, ILogger logger)
    {
        ClaimsIdentity? identity = null;

        if (context.Request.Headers.TryGetValue(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, out StringValues headerValues) && headerValues.Count == 1)
        {
            try
            {
                string encodedPrincipalData = headerValues.ToString();
                byte[] decodedPrincpalData = Convert.FromBase64String(encodedPrincipalData);
                string json = Encoding.UTF8.GetString(decodedPrincpalData);
                AppServiceClientPrincipal? principal = JsonSerializer.Deserialize<AppServiceClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (!string.IsNullOrEmpty(principal?.Auth_typ))
                {
                    // When Name_typ and Role_type are null, ClaimsIdentity contructor uses default values.
                    // Auth_typ must not be null or empty for ClaimsIdentity.IsAuthenticated() to be true.
                    // Whitespace is not a requirement per: https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsidentity.isauthenticated?view=net-6.0#remarks
                    identity = new(principal.Auth_typ, principal.Name_typ, principal.Role_typ);

                    if (principal.Claims is not null && principal.Claims.Any())
                    {
                        identity.AddClaims(principal.Claims
                            .Where(claim => claim.Typ is not null && claim.Val is not null)
                            .Select(claim => new Claim(type: claim.Typ!, value: claim.Val!))
                            );
                    }
                }
            }
            catch (Exception error) when (
                error is JsonException ||
                error is ArgumentNullException ||
                error is NotSupportedException ||
                error is InvalidOperationException)
            {
                // Logging the parsing failure exception to the console, but not rethrowing
                // nor creating a DataApiBuilder exception because the authentication handler
                // will create and send a 401 unauthorized response to the client.
                logger.LogError(
                    exception: error,
                    message: "{correlationId} Failure processing the AppService EasyAuth header: {errorMessage}",
                    HttpContextExtensions.GetLoggerCorrelationId(context),
                    error.Message);
            }
        }

        return identity;
    }
}

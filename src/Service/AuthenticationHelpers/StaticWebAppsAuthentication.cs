using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
{
    /// <summary>
    /// Helper class which parses EasyAuth's injected headers into a ClaimsIdentity object.
    /// This class provides helper methods for StaticWebApps' Authentication feature: EasyAuth.
    /// </summary>
    public class StaticWebAppsAuthentication
    {
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
            public IEnumerable<SWAPrincipalClaim>? Claims { get; set; }
        }

        /// <summary>
        /// Representation of a user claim in a SWA token payload. 
        /// </summary>
        public class SWAPrincipalClaim
        {
            public string? Typ { get; set; }
            public string? Val { get; set; }
        }

        /// <summary>
        /// Base64 decodes and deserializes the x-ms-client-principal payload containing
        /// SWA token metadata.
        /// Writes SWA token metadata (roles and token claims) to .NET ClaimsIdentity object.
        /// </summary>
        /// <param name="context"></param>
        /// <returns>ClaimsIdentity containing authentication metadata.</returns>
        public static ClaimsIdentity? Parse(HttpContext context)
        {
            ClaimsIdentity? identity = null;
            StaticWebAppsClientPrincipal principal = new();
            try
            {
                if (context.Request.Headers.TryGetValue(AuthenticationConfig.CLIENT_PRINCIPAL_HEADER, out StringValues headerPayload))
                {
                    string data = headerPayload[0];
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

                identity = new(principal.IdentityProvider);
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, principal.UserId ?? string.Empty));
                identity.AddClaim(new Claim(ClaimTypes.Name, principal.UserDetails ?? string.Empty));
                identity.AddClaims(principal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));

                // Copy all SWA token claims to .NET ClaimsIdentity object.
                if (principal.Claims is not null && principal.Claims.Any())
                {
                    identity.AddClaims(principal.Claims
                        .Where(claim => claim.Typ is not null && claim.Val is not null)
                        .Select(claim => new Claim(type: claim.Typ!, value: claim.Val!))
                        );
                }

                return identity;
            }
            catch (Exception error) when (
                error is JsonException ||
                error is ArgumentNullException ||
                error is NotSupportedException ||
                error is InvalidOperationException)
            {
                // Log any SWA token processing failures to the console.
                // Does not raise or rethrow a DataApiBuilder exception because
                // the authentication handler caller will return a 401 unauthorized
                // response to the client.
                Console.Error.WriteLine("Failure processing the StaticWebApps EasyAuth header.");
                Console.Error.WriteLine(error.Message);
                Console.Error.WriteLine(error.StackTrace);
            }

            return identity;
        }
    }
}

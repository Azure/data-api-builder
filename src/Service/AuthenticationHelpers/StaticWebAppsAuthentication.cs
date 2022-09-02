using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
        }

        public static ClaimsIdentity? Parse(HttpContext context, ILogger logger)
        {
            ClaimsIdentity? identity = null;
            StaticWebAppsClientPrincipal principal = new();
            try
            {
                if (context.Request.Headers.TryGetValue(AuthenticationConfig.CLIENT_PRINCIPAL_HEADER, out StringValues header))
                {
                    string data = header[0];
                    byte[] decoded = Convert.FromBase64String(data);
                    string json = Encoding.UTF8.GetString(decoded);
                    principal = JsonSerializer.Deserialize<StaticWebAppsClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }

                if (!principal?.UserRoles?.Any() ?? true)
                {
                    return identity;
                }

                identity = new(principal!.IdentityProvider);
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, principal.UserId ?? string.Empty));
                identity.AddClaim(new Claim(ClaimTypes.Name, principal.UserDetails ?? string.Empty));
                identity.AddClaims(principal.UserRoles!.Select(r => new Claim(ClaimTypes.Role, r)));

                return identity;
            }
            catch (Exception error)
            {
                // Logging the parsing failure exception to the console, but not rethrowing
                // nor creating a DataApiBuilder exception because the authentication handler
                // will create and send a 401 unauthorized response to the client.
                logger.LogError($"Failure processing the StaticWebApps EasyAuth header.\n" +
                    $"{error.Message}\n" +
                    $"{error.StackTrace}");
            }

            return identity;
        }
    }
}

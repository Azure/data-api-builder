using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataGateway.Service.AuthenticationHelpers
{
    public class StaticWebAppsAuthentication
    {
        public const string EASYAUTHHEADER = "X-MS-CLIENT-PRINCIPAL";

        public class StaticWebAppsClientPrincipal
        {
            public string? IdentityProvider { get; set; }
            public string? UserId { get; set; }
            public string? UserDetails { get; set; }
            public IEnumerable<string>? UserRoles { get; set; }
        }

        public static ClaimsIdentity? Parse(HttpRequest req)
        {
            ClaimsIdentity? identity = null;
            StaticWebAppsClientPrincipal principal = new();
            try
            {
                if (req.Headers.TryGetValue(EASYAUTHHEADER, out StringValues header))
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
                // nor creating a DataGateway exception because the authentication handler
                // will create and send a 401 unauthorized response to the client.
                Console.Error.WriteLine("Failure processing the StaticWebApps EasyAuth header.");
                Console.Error.WriteLine(error.Message);
                Console.Error.WriteLine(error.StackTrace);
            }

            return identity;
        }
    }
}

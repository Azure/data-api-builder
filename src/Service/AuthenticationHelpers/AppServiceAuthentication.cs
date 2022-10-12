using System;
using System.Collections.Generic;
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
    /// This class provides helper methods for AppService's Authentication feature: EasyAuth.
    /// </summary>
    public static class AppServiceAuthentication
    {
        /// <summary>
        /// Representation of authenticated user principal Http header
        /// injected by EasyAuth
        /// </summary>
        public struct AppServiceClientPrincipal
        {
            public string Auth_typ { get; set; }
            public string Name_typ { get; set; }
            public string Role_typ { get; set; }
            public IEnumerable<AppServiceClaim> Claims { get; set; }
        }

        /// <summary>
        /// Representation of authenticated user principal claims
        /// injected by EasyAuth
        /// </summary>
        public struct AppServiceClaim
        {
            public string Typ { get; set; }
            public string Val { get; set; }
        }

        /// <summary>
        /// Create ClaimsIdentity object from EasyAuth
        /// injected x-ms-client-principal injected header,
        /// the value is a base64 encoded custom JWT injected by EasyAuth
        /// as a result of validating a bearer token.
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

            if (context.Request.Headers.TryGetValue(AuthenticationConfig.CLIENT_PRINCIPAL_HEADER, out StringValues header))
            {
                try
                {
                    string encodedPrincipalData = header[0];
                    byte[] decodedPrincpalData = Convert.FromBase64String(encodedPrincipalData);
                    string json = Encoding.UTF8.GetString(decodedPrincpalData);
                    AppServiceClientPrincipal principal = JsonSerializer.Deserialize<AppServiceClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    identity = new(principal.Auth_typ, principal.Name_typ, principal.Role_typ);

                    if (principal.Claims != null)
                    {
                        foreach (AppServiceClaim claim in principal.Claims)
                        {
                            identity.AddClaim(new Claim(type: claim.Typ, value: claim.Val));
                        }
                    }
                }
                catch (Exception error)
                {
                    // Logging the parsing failure exception to the console, but not rethrowing
                    // nor creating a DataApiBuilder exception because the authentication handler
                    // will create and send a 401 unauthorized response to the client.
                    logger.LogError($"Failure processing the AppService EasyAuth header.\n" +
                        $"{error.Message}\n" +
                        $"{error.StackTrace}");
                }
            }

            return identity;
        }
    }
}

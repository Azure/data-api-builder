using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataGateway.Service.AuthenticationHelpers
{
    /// <summary>
    /// Helper class which parses EasyAuth's injected headers into a ClaimsIdentity object.
    /// This class provides helper methods for StaticWebApp's Authentication feature: EasyAuth.
    /// </summary>
    public static class EasyAuthAuthentication
    {
        /// <summary>
        /// Representation of authenticated user principal Http header
        /// injected by EasyAuth
        /// </summary>
        private struct EasyAuthClientPrincipal
        {
            public string Auth_typ { get; set; }
            public string Name_typ { get; set; }
            public string Role_typ { get; set; }
            public IEnumerable<EasyAuthClaim> Claims { get; set; }
        }

        /// <summary>
        /// Representation of authenticated user principal claims
        /// injected by EasyAuth
        /// </summary>
        private struct EasyAuthClaim
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
        /// <returns></returns>
        public static ClaimsIdentity? Parse(HttpContext context)
        {
            ClaimsIdentity? identity = null;

            if (context.Request.Headers.TryGetValue("x-ms-client-principal", out StringValues header))
            {
                string encodedPrincipalData = header[0];
                byte[] decodedPrincpalData = Convert.FromBase64String(encodedPrincipalData);
                string json = Encoding.UTF8.GetString(decodedPrincpalData);
                EasyAuthClientPrincipal principal = JsonSerializer.Deserialize<EasyAuthClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                identity = new(principal.Auth_typ, principal.Name_typ, principal.Role_typ);

                if (principal.Claims != null)
                {
                    foreach (EasyAuthClaim claim in principal.Claims)
                    {
                        identity.AddClaim(new Claim(type: claim.Typ, value: claim.Val));
                    }
                }
            }

            return identity;
        }
    }
}

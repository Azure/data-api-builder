using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// Helper class which parses AppService's injected headers into a ClaimsIdentity object
    /// </summary>
    public static class AppServiceAuthentication
    {
        /// <summary>
        /// Representation of authenticated user principal Http header
        /// injected by AppService Authentication (EasyAuth)
        /// </summary>
        private struct AppServiceClientPrincipal
        {
            public string Auth_typ { get; set; }
            public string Name_typ { get; set; }
            public string Role_typ { get; set; }
            public IEnumerable<AppServiceClaim> Claims { get; set; }
        }

        /// <summary>
        /// Representation of authenticated user principal claims
        /// injected by AppService Authentication (EasyAuth)
        /// </summary>
        private struct AppServiceClaim
        {
            public string Typ { get; set; }
            public string Val { get; set; }
        }

        /// <summary>
        /// Create ClaimsIdentity object from AppService Authentication (EasyAuth)
        /// injected x-ms-client-principal injected header
        /// </summary>
        /// <param name="context">Request's Http Context</param>
        /// <returns></returns>
        public static ClaimsIdentity Parse(HttpContext context)
        {
            // x-ms-client-principal is base64 encoded custom JWT injected by AppService Authentication (EasyAuth)
            // only when Bearer token has been validated.
            if (context.Request.Headers.TryGetValue("x-ms-client-principal", out StringValues header))
            {
                string encodedPrincipalData = header[0];
                byte[] decodedPrincpalData = Convert.FromBase64String(encodedPrincipalData);
                string json = Encoding.UTF8.GetString(decodedPrincpalData);
                AppServiceClientPrincipal principal = JsonSerializer.Deserialize<AppServiceClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                ClaimsIdentity identity = new(principal.Auth_typ, principal.Name_typ, principal.Role_typ);

                if (principal.Claims is not null && principal.Claims.Count() > 0)
                {
                    foreach (AppServiceClaim claim in principal.Claims)
                    {
                        identity.AddClaim(new Claim(type: claim.Typ, value: claim.Val));
                    }
                }

                return identity;
            }

            return null;
        }
    }
}

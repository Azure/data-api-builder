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
        public const string EASYAUTHHEADER = "X-MS-CLIENT-PRINCIPAL";
        /// <summary>
        /// Representation of authenticated user principal Http header
        /// injected by EasyAuth
        /// </summary>
        public struct EasyAuthClientPrincipal
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
        public struct EasyAuthClaim
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
        public static ClaimsIdentity? Parse(HttpContext context)
        {
            ClaimsIdentity? identity = null;

            if (context.Request.Headers.TryGetValue(EasyAuthAuthentication.EASYAUTHHEADER, out StringValues header))
            {
                try
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
                catch(Exception error)
                {
                    Console.Error.WriteLine("Failure processing the EasyAuth header.");
                    Console.Error.WriteLine(error.Message);
                    Console.Error.WriteLine(error.StackTrace);

                    //throw new DataGatewayException(
                    //    message: "Invalid EasyAuth header",
                    //    statusCode: HttpStatusCode.Unauthorized,
                    //    subStatusCode: DataGatewayException.SubStatusCodes.AuthenticationChallenge
                    //    );
                }
            }

            return identity;
        }
    }
}

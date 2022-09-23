using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using static Azure.DataApiBuilder.Service.AuthenticationHelpers.AppServiceAuthentication;
using static Azure.DataApiBuilder.Service.AuthenticationHelpers.StaticWebAppsAuthentication;

namespace Azure.DataApiBuilder.Service.Tests
{
    internal static class AuthTestHelper
    {
        /// <summary>
        /// Creates a mocked EasyAuth token, namely, the value of the header injected by EasyAuth.
        /// </summary>
        /// <returns>A Base64 encoded string of a serialized EasyAuthClientPrincipal object</returns>
        public static string CreateAppServiceEasyAuthToken()
        {
            AppServiceClaim emailClaim = new()
            {
                Val = "apple@contoso.com",
                Typ = ClaimTypes.Upn
            };

            AppServiceClaim roleClaimAnonymous = new()
            {
                Val = "Anonymous",
                Typ = ClaimTypes.Role
            };

            AppServiceClaim roleClaimAuthenticated = new()
            {
                Val = "Authenticated",
                Typ = ClaimTypes.Role
            };

            List<AppServiceClaim> claims = new();
            claims.Add(emailClaim);
            claims.Add(roleClaimAnonymous);
            claims.Add(roleClaimAuthenticated);

            AppServiceClientPrincipal token = new()
            {
                Auth_typ = "aad",
                Name_typ = "Apple Banana",
                Claims = claims,
                Role_typ = ClaimTypes.Role
            };

            string serializedToken = JsonSerializer.Serialize(value: token);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedToken));
        }

        /// <summary>
        /// Creates a mocked EasyAuth token, namely, the value of the header injected by EasyAuth.
        /// </summary>
        /// <param name="addAuthenticated">Whether to conditionally add the authenticated and/or other custom roles</param>
        /// <param name="specificRole">The name of the custom role to add to the token payload</param>
        /// <param name="claims">Collection of claims to include in SWA token payload.</param>
        /// <returns>A Base64 encoded string of a serialized StaticWebAppsClientPrincipal object</returns>
        public static string CreateStaticWebAppsEasyAuthToken(
            bool addAuthenticated = true,
            string specificRole = null,
            IEnumerable<SWAPrincipalClaim> claims = null)
        {
            // The anonymous role is present in all requests sent to Static Web Apps or AppService endpoints.
            List<string> roles = new()
            {
                "anonymous"
            };

            if (addAuthenticated)
            {
                if (specificRole is null)
                {
                    roles.Add("authenticated");
                    roles.Add("policy_tester_01");
                    roles.Add("policy_tester_02");
                    roles.Add("policy_tester_03");
                    roles.Add("policy_tester_04");
                    roles.Add("policy_tester_05");
                    roles.Add("policy_tester_06");
                    roles.Add("policy_tester_07");
                    roles.Add("policy_tester_08");
                    roles.Add("policy_tester_09");
                    roles.Add("policy_tester_update_noread");
                }
                else
                {
                    roles.Add(specificRole);
                }
            }

            StaticWebAppsClientPrincipal token = new()
            {
                IdentityProvider = "github",
                UserRoles = roles,
                Claims = claims
            };

            string serializedToken = JsonSerializer.Serialize(value: token);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedToken));
        }
    }
}

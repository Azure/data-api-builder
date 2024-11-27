// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using static Azure.DataApiBuilder.Core.AuthenticationHelpers.AppServiceAuthentication;
using static Azure.DataApiBuilder.Core.AuthenticationHelpers.StaticWebAppsAuthentication;

namespace Azure.DataApiBuilder.Service.Tests
{
    internal static class AuthTestHelper
    {
        /// <summary>
        /// Creates a mocked EasyAuth token, namely, the value of the header injected by EasyAuth.
        /// </summary>
        /// <param name="nameClaimType">Defines the ClaimType of the claim used for the return value of Identity.Name </param>
        /// <param name="roleClaimType">Defines the ClaimType of the claim used for the return value of ClaimsPrincpal.IsInRole(roleName)</param>
        /// <returns>A Base64 encoded string of a serialized EasyAuthClientPrincipal object</returns>
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsidentity.nameclaimtype"/>
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsidentity.roleclaimtype"/>
        public static string CreateAppServiceEasyAuthToken(
            string? nameClaimType = ClaimTypes.Name,
            string? roleClaimType = ClaimTypes.Role,
            IEnumerable<AppServiceClaim>? additionalClaims = null)
        {
            AppServiceClaim emailClaim = new()
            {
                Val = "apple@contoso.com",
                Typ = ClaimTypes.Upn
            };

            AppServiceClaim roleClaimAnonymous = new()
            {
                Val = "Anonymous",
                Typ = roleClaimType
            };

            AppServiceClaim roleClaimAuthenticated = new()
            {
                Val = "Authenticated",
                Typ = roleClaimType
            };

            AppServiceClaim roleClaimShortNameClaimType = new()
            {
                Val = "RoleShortClaimType",
                Typ = "roles"
            };

            AppServiceClaim roleClaimUriClaimType = new()
            {
                Val = "RoleUriClaimType",
                Typ = ClaimTypes.Role
            };

            AppServiceClaim nameShortClaimType = new()
            {
                Val = "NameShortClaimType",
                Typ = "unique_name"
            };

            AppServiceClaim nameUriClaimType = new()
            {
                Val = "NameUriClaimType",
                Typ = ClaimTypes.Name
            };

            HashSet<AppServiceClaim> claims = new()
            {
                emailClaim,
                roleClaimAnonymous,
                roleClaimAuthenticated,
                roleClaimShortNameClaimType,
                roleClaimUriClaimType,
                nameShortClaimType,
                nameUriClaimType
            };

            if (additionalClaims != null)
            {
                claims.UnionWith(additionalClaims);
            }

            AppServiceClientPrincipal token = new()
            {
                Auth_typ = "aad",
                Name_typ = nameClaimType,
                Claims = claims,
                Role_typ = roleClaimType
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
            string? specificRole = null,
            string? userId = null,
            string? userDetails = null)
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
                UserId = userId,
                UserDetails = userDetails,
                IdentityProvider = "aad",
                UserRoles = roles
            };

            string serializedToken = JsonSerializer.Serialize(value: token);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedToken));
        }
    }
}

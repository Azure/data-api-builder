using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public class PolicyHelper
    {
        public static string CONTEXT_POLICY_KEY = "X-DG-Policy";

        //List<string> claimsToSubstitute, HttpContext context
        public static string ProcessTokenClaimsForPolicy(string policy, HttpContext context)
        {
            // Start "@claims().user_ID eq email
            // Find and replace instances of @claims.claim with claimValue
            return "1234 eq email";
        }

        private static string GetClaimValue(string claimName, HttpContext context)
        {
            return context.User.FindFirst(claimName)?.Value.ToString();
        }
    }
}

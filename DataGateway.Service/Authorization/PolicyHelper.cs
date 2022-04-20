using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public class PolicyHelper
    {
        //List<string> claimsToSubstitute, HttpContext context
        public static string ProcessTokenClaimsForPolicy(string policy, HttpContext context)
        {
            // Start "@claims().user_ID eq email

            return "1234 eq email";
        }
    }
}

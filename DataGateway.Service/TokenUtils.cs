using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service
{
    public static class TokenUtils
    {
        public static string HeaderPrefix = "Bearer ";
        public static JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();

        public static string GetRawAadToken(string aadToken)
        {
            if (aadToken != null && aadToken.StartsWith(HeaderPrefix))
            {
                aadToken = aadToken.Remove(0, HeaderPrefix.Length);
            }
            return aadToken;
        }

        public static string GetTenantId(string aadToken)
        {
            return ExtractFromClaim(aadToken, "tid");
        }

        public static string GetUserId(string aadToken)
        {
            return ExtractFromClaim(aadToken, "puid");
        }

        public static string GetUserName(string aadToken)
        {
            return ExtractFromClaim(aadToken, "name");
        }

        private static string ExtractFromClaim(string token, string claim)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }
            SecurityToken jsonToken = handler.ReadToken(token);
            JwtSecurityToken jwtToken = jsonToken as JwtSecurityToken;
            string value = jwtToken.Claims.First(c => c.Type == claim)?.Value;
            return value;
        }
    }
}

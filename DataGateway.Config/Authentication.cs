namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Authentication configuration.
    /// </summary>
    /// <param name="Provider">Identity Provider. Default is EasyAuth.
    /// With EasyAuth, no Audience or Issuer are expected.
    /// </param>
    /// <param name="Jwt">Settings enabling validation of the received JWT token.
    /// Required only when Provider is other than EasyAuth.</param>
    public record AuthenticationConfig(
        string Provider = AuthenticationConfig.EASYAUTH_PROVIDER_NAME,
        Jwt? Jwt = null)
    {
        public const string EASYAUTH_PROVIDER_NAME = "EasyAuth";

        public bool IsEasyAuthAuthenticationProvider()
        {
            return Provider.Equals(EASYAUTH_PROVIDER_NAME);
        }
    }

    /// <summary>
    /// Settings useful for validating the received Json Web Token (JWT).
    /// </summary> 
    /// <param name="Audience"></param>
    /// <param name="Issuer"></param>
    public record Jwt(string Audience, string Issuer);
}

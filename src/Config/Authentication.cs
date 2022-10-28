namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// Authentication configuration.
    /// </summary>
    /// <param name="Provider">Identity Provider. Default is StaticWebApps.
    /// With EasyAuth, no Audience or Issuer are expected.
    /// </param>
    /// <param name="Jwt">Settings enabling validation of the received JWT token.
    /// Required only when Provider is other than EasyAuth.</param>
    public record AuthenticationConfig(
        string Provider,
        Jwt? Jwt = null)
    {
        public const string CLIENT_PRINCIPAL_HEADER = "X-MS-CLIENT-PRINCIPAL";
        public const string ROLE_CLAIM_TYPE = "roles";
        public bool IsEasyAuthAuthenticationProvider()
        {
            return Enum.GetNames(typeof(EasyAuthType)).Any(x => x.Equals(Provider, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Settings useful for validating the received Json Web Token (JWT).
    /// </summary>
    /// <param name="Audience"></param>
    /// <param name="Issuer"></param>
    public record Jwt(string Audience, string Issuer);

    /// <summary>
    /// Different modes in which the runtime can run.
    /// </summary>
    public enum EasyAuthType
    {
        StaticWebApps,
        AppService
    }
}

namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Authentication configuration.
    /// </summary>
    /// <param name="Provider">Identity Provider.
    /// With EasyAuth, no Audience or Issuer are expected.
    /// </param>
    /// <param name="Jwt">Settings enabling validation of the received JWT token.
    /// Required only when Provider is other than EasyAuth.</param>
    public record AuthenticationConfig(
        string Provider,
        Jwt? Jwt = null)
    {
        public bool IsEasyAuthAuthenticationProvider()
        {
            return Enum.GetName(EasyAuthType.StaticWebApps)!.Equals(Provider, StringComparison.OrdinalIgnoreCase)
                || Enum.GetName(EasyAuthType.AppService)!.Equals(Provider, StringComparison.OrdinalIgnoreCase);
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

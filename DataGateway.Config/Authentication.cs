namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Authentication configuration.
    /// </summary>
    /// <param name="Provider">Identity Provider. Default is EasyAuth.</param>

    /// <param name="Jwt">Settings enabling validation of the received JWT token.
    /// Required only when Provider is other than EasyAuth.</param>
    public record AuthenticationConfig(
        string Provider = "EasyAuth",
        Jwt? Jwt = null);

    /// <summary>
    /// Settings useful for validating the received Json Web Token (JWT).
    /// </summary> 
    /// <param name="Audience"></param>
    /// <param name="Issuer"></param>
    /// <param name="IssuerKey"></param>
    public record Jwt(string Audience, string Issuer, string IssuerKey);
}

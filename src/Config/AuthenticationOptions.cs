namespace Azure.DataApiBuilder.Config;

public enum EasyAuthType
{
    StaticWebApps,
    AppService
}

public record AuthenticationOptions(string Provider, JwtOptions? Jwt)
{
    private const string SIMULATOR_AUTHENTICATION = "Simulator";
    public const string CLIENT_PRINCIPAL_HEADER = "X-MS-CLIENT-PRINCIPAL";
    public const string NAME_CLAIM_TYPE = "name";
    public const string ROLE_CLAIM_TYPE = "roles";

    /// <summary>
    /// Returns whether the configured Provider matches an
    /// EasyAuth authentication type.
    /// </summary>
    /// <returns>True if Provider is an EasyAuth type.</returns>
    public bool IsEasyAuthAuthenticationProvider()
    {
        return Enum.GetNames(typeof(EasyAuthType)).Any(x => x.Equals(Provider, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns whether the configured Provider value matches
    /// the AuthenticateDevModeRquests EasyAuth type.
    /// </summary>
    /// <returns>True when development mode should authenticate all requests.</returns>
    public bool IsAuthenticationSimulatorEnabled()
    {
        return Provider.Equals(SIMULATOR_AUTHENTICATION, StringComparison.OrdinalIgnoreCase);
    }
};

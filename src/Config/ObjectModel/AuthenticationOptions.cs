// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Authentication configuration.
/// </summary>
/// <param name="Provider">Identity Provider. Default is StaticWebApps.
/// With EasyAuth and Simulator, no Audience or Issuer are expected.
/// </param>
/// <param name="Jwt">Settings enabling validation of the received JWT token.
/// Required only when Provider is other than EasyAuth.</param>
public record AuthenticationOptions(string Provider = nameof(EasyAuthType.StaticWebApps), JwtOptions? Jwt = null)
{
    public const string SIMULATOR_AUTHENTICATION = "Simulator";
    public const string CLIENT_PRINCIPAL_HEADER = "X-MS-CLIENT-PRINCIPAL";
    public const string NAME_CLAIM_TYPE = "name";
    public const string ROLE_CLAIM_TYPE = "roles";
    public const string ORIGINAL_ROLE_CLAIM_TYPE = "original_roles";

    /// <summary>
    /// Returns whether the configured Provider matches an
    /// EasyAuth authentication type.
    /// </summary>
    /// <returns>True if Provider is an EasyAuth type.</returns>
    public bool IsEasyAuthAuthenticationProvider() => Enum.GetNames(typeof(EasyAuthType)).Any(x => x.Equals(Provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns whether the configured Provider value matches the simulator authentication type.
    /// </summary>
    /// <returns>True when development mode should authenticate all requests.</returns>
    public bool IsAuthenticationSimulatorEnabled() => Provider.Equals(SIMULATOR_AUTHENTICATION, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A shorthand method to determine whether JWT is configured for the current authentication provider.
    /// </summary>
    /// <returns>True if the provider is enabled for JWT, otherwise false.</returns>
    public bool IsJwtConfiguredIdentityProvider() => !IsEasyAuthAuthenticationProvider() && !IsAuthenticationSimulatorEnabled();
};

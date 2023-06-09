// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record AuthenticationOptions(string Provider, JwtOptions? Jwt)
{
    public const string SIMULATOR_AUTHENTICATION = "Simulator";
    public const string CLIENT_PRINCIPAL_HEADER = "X-MS-CLIENT-PRINCIPAL";
    public const string NAME_CLAIM_TYPE = "name";
    public const string ROLE_CLAIM_TYPE = "roles";

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

    public bool IsJwtConfiguredIdentityProvider() => !IsEasyAuthAuthenticationProvider() && !IsAuthenticationSimulatorEnabled();
};

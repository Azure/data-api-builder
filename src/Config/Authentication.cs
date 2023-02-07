// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: Authentication.cs
// **************************************

namespace Azure.DataApiBuilder.Config
{
    /// <summary>
    /// Authentication configuration.
    /// </summary>
    /// <param name="Provider">Identity Provider. Default is StaticWebApps.
    /// With EasyAuth and Simulator, no Audience or Issuer are expected.
    /// </param>
    /// <param name="Jwt">Settings enabling validation of the received JWT token.
    /// Required only when Provider is other than EasyAuth.</param>
    public record AuthenticationConfig(
        string Provider,
        Jwt? Jwt = null)
    {
        public const string CLIENT_PRINCIPAL_HEADER = "X-MS-CLIENT-PRINCIPAL";
        public const string NAME_CLAIM_TYPE = "name";
        public const string ROLE_CLAIM_TYPE = "roles";
        public const string SIMULATOR_AUTHENTICATION = "Simulator";

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
    }

    /// <summary>
    /// Settings useful for validating the received Json Web Token (JWT).
    /// </summary>
    /// <param name="Audience"></param>
    /// <param name="Issuer"></param>
    public record Jwt(string? Audience, string? Issuer);

    /// <summary>
    /// Various EasyAuth modes in which the runtime can run.
    /// </summary>
    public enum EasyAuthType
    {
        StaticWebApps,
        AppService
    }
}

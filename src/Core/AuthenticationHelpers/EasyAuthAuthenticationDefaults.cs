// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// EasyAuth authentication scheme names granularized by provider
/// to enable compatibility with HotReloading authentication settings.
/// Authentication schemes:
/// - Correlate to an authentication handler
/// - Indicate to AuthenticateAsync which handler to use
/// </summary>
/// <seealso cref="https://learn.microsoft.com/azure/app-service/overview-authentication-authorization"/>
public static class EasyAuthAuthenticationDefaults
{
    /// <summary>
    /// Used in ConfigureAuthentication() (AuthV1)
    /// The default value used for EasyAuthAuthenticationOptions.AuthenticationScheme.
    /// </summary>
    public const string AUTHENTICATIONSCHEME = "EasyAuthAuthentication";

    public const string SWAAUTHSCHEME = "StaticWebAppsAuthentication";

    public const string APPSERVICEAUTHSCHEME = "AppServiceAuthentication";

    /// <summary>
    /// Warning message emitted when the EasyAuth payload is invalid.
    /// </summary>
    public const string INVALID_PAYLOAD_ERROR = "Invalid EasyAuth Payload.";
}

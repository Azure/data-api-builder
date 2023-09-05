// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record HostOptions(CorsOptions? Cors, AuthenticationOptions? Authentication, HostMode Mode = HostMode.Development)
{
    /// <summary>
    /// Returns the default host Global Settings
    /// If the user doesn't specify host mode. Default value to be used is Development.
    /// Sample:
    // "host": {
    //     "mode": "development",
    //     "cors": {
    //         "origins": [],
    //         "allow-credentials": true
    //     },
    //     "authentication": {
    //         "provider": "StaticWebApps"
    //     }
    // }
    /// </summary>
    public static HostOptions GetDefaultHostOptions(
        HostMode hostMode,
        IEnumerable<string>? corsOrigin,
        string authenticationProvider,
        string? audience,
        string? issuer)
    {
        string[]? corsOriginArray = corsOrigin is null ? new string[] { } : corsOrigin.ToArray();
        CorsOptions cors = new(Origins: corsOriginArray);
        AuthenticationOptions AuthenticationOptions;
        if (Enum.TryParse<EasyAuthType>(authenticationProvider, ignoreCase: true, out _)
            || AuthenticationOptions.SIMULATOR_AUTHENTICATION.Equals(authenticationProvider))
        {
            AuthenticationOptions = new(Provider: authenticationProvider, Jwt: null);
        }
        else
        {
            AuthenticationOptions = new(
                Provider: authenticationProvider,
                Jwt: new(audience, issuer)
            );
        }

        return new(
            Mode: hostMode,
            Cors: cors,
            Authentication: AuthenticationOptions);
    }
}

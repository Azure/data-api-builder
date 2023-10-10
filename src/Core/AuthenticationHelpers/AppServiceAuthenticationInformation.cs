// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// Info about the App Services configuration on the host. This class is an abridged mirror of
/// Microsoft.Identity.Web's AppServicesAuthenticationInformation.cs helper class used to
/// detect whether the app is running in an Azure App Service environment.
/// </summary>
/// <seealso cref="https://github.com/AzureAD/microsoft-identity-web/blob/master/src/Microsoft.Identity.Web/AppServicesAuth/AppServicesAuthenticationInformation.cs"/>
public static class AppServiceAuthenticationInfo
{
    /// <summary>
    /// Environment variable key whose value represents whether AppService EasyAuth is enabled ("true" or "false").
    /// </summary>
    public const string APPSERVICESAUTH_ENABLED_ENVVAR = "WEBSITE_AUTH_ENABLED";
    /// <summary>
    /// Environment variable key whose value represents Identity Provider such as "AzureActiveDirectory"
    /// </summary>
    public const string APPSERVICESAUTH_IDENTITYPROVIDER_ENVVAR = "WEBSITE_AUTH_DEFAULT_PROVIDER";
    /// <summary>
    /// Error message used when AppService Authentication is configured in production mode in a non AppService Environment.
    /// </summary>
    public const string APPSERVICE_PROD_MISSING_ENV_CONFIG = "AppService environment not detected while runtime is in production mode.";
    /// <summary>
    /// Warning message logged when AppService environment not detected (applicable to development mode).
    /// </summary>
    public const string APPSERVICE_DEV_MISSING_ENV_CONFIG = "AppService environment not detected, EasyAuth authentication may not behave as expected.";

    /// <summary>
    /// Returns a best guess whether AppService is enabled in the environment by checking for
    /// existence and value population of known AppService environment variables.
    /// This check is determined to be "best guess" because environment variables could be
    /// manually added or overridden.
    /// This check's purpose is to help warn developers that an AppService environment is not detected
    /// where the DataApiBuilder service is executing and DataApiBuilder is configured to use AppService
    /// as the identity provider.
    /// </summary>
    public static bool AreExpectedAppServiceEnvVarsPresent()
    {
        string? appServiceEnabled = Environment.GetEnvironmentVariable(APPSERVICESAUTH_ENABLED_ENVVAR);
        string? appServiceIdentityProvider = Environment.GetEnvironmentVariable(APPSERVICESAUTH_IDENTITYPROVIDER_ENVVAR);

        if (string.IsNullOrEmpty(appServiceEnabled) || string.IsNullOrEmpty(appServiceIdentityProvider))
        {
            return false;
        }

        return appServiceEnabled.Equals(value: "true", comparisonType: StringComparison.OrdinalIgnoreCase);
    }
}

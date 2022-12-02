using System;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
{
    /// <summary>
    /// Information about the App Services configuration on the host. This class is an abridged mirror of
    /// Microsoft.Identity.Web's AppServicesAuthenticationInformation.cs helper class used to
    /// detect whether the app is running in an Azure App Service environment.
    /// </summary>
    /// <seealso cref="https://github.com/AzureAD/microsoft-identity-web/blob/master/src/Microsoft.Identity.Web/AppServicesAuth/AppServicesAuthenticationInformation.cs"/>
    public static class AppServicesAuthenticationInformation
    {
        /// <summary>
        /// Environment variable key whose value represents whether AppService EasyAuth is enabled ("true" or "false").
        /// </summary>
        private const string APPSERVICESAUTH_ENABLED_ENVIRONMENTVARIABLE = "WEBSITE_AUTH_ENABLED";
        /// <summary>
        /// Environment variable key whose value represents Identity Provider such as "AzureActiveDirectory"
        /// </summary>
        private const string APPSERVICESAUTH_IDENTITYPROVIDER_ENVIRONMENTVARIABLE = "WEBSITE_AUTH_DEFAULT_PROVIDER";

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
            string? appServiceEnabled = Environment.GetEnvironmentVariable(APPSERVICESAUTH_ENABLED_ENVIRONMENTVARIABLE);
            string? appServiceIdentityProvider = Environment.GetEnvironmentVariable(APPSERVICESAUTH_IDENTITYPROVIDER_ENVIRONMENTVARIABLE);

            if (string.IsNullOrEmpty(appServiceEnabled) || string.IsNullOrEmpty(appServiceIdentityProvider))
            {
                return false;
            }

            return appServiceEnabled.Equals(value: "true", comparisonType: StringComparison.OrdinalIgnoreCase);
        }
    }
}

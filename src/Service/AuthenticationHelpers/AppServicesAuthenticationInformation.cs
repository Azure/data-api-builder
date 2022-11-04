using System;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
{
    /// <summary>
    /// Information about the App Services configuration on the host.
    /// This class is an abridged mirror of Microsoft.Identity.Web's
    /// AppServicesAuthenticationInformation.cs helper class used to
    /// detect whether the app is running in an Azure App Service environment.
    /// </summary>
    /// <seealso cref="https://github.com/AzureAD/microsoft-identity-web/blob/master/src/Microsoft.Identity.Web/AppServicesAuth/AppServicesAuthenticationInformation.cs"/>
    public static class AppServicesAuthenticationInformation
    {
        // Environment variables.
        internal const string APPSERVICESAUTH_ENABLED_ENVIRONMENTVARIABLE = "WEBSITE_AUTH_ENABLED";            // True
        internal const string APPSERVICESAUTH_OPENIDISSUER_ENVIRONMENTVARIABLE = "WEBSITE_AUTH_OPENID_ISSUER"; // for instance https://sts.windows.net/<tenantId>/
        internal const string APPSERVICESAUTH_CLIENTID_ENVIRONMENTVARIABLE = "WEBSITE_AUTH_CLIENT_ID";         // A GUID
        internal const string APPSERVICESAUTH_CLIENTSECRET_ENVIRONMENTVARIABLE = "WEBSITE_AUTH_CLIENT_SECRET"; // A string
        internal const string APPSERVICESAUTH_CLIENTSECRET_SETTINGNAME = "WEBSITE_AUTH_CLIENT_SECRET_SETTING_NAME"; // A string
        internal const string APPSERVICESAUTH_LOGOUTPATH_ENVIRONMENTVARIABLE = "WEBSITE_AUTH_LOGOUT_PATH";    // /.auth/logout
        internal const string APPSERVICESAUTH_IDENTITYPROVIDER_ENVIRONMENTVARIABLE = "WEBSITE_AUTH_DEFAULT_PROVIDER"; // AzureActiveDirectory
        internal const string APPSERVICESAUTH_AZUREACTIVEDIRECTORY = "AzureActiveDirectory";
        internal const string APPSERVICESAUTH_AAD = "AAD";

        /// <summary>
        /// Returns whether App Services authentication is enabled?
        /// </summary>
        public static bool IsAppServicesAadAuthenticationEnabled
        {
            get
            {
                return
                    string.Equals(
                        Environment.GetEnvironmentVariable(APPSERVICESAUTH_ENABLED_ENVIRONMENTVARIABLE),
                        "true",
                        StringComparison.OrdinalIgnoreCase) &&
                     (string.Equals(
                         Environment.GetEnvironmentVariable(APPSERVICESAUTH_IDENTITYPROVIDER_ENVIRONMENTVARIABLE),
                         APPSERVICESAUTH_AZUREACTIVEDIRECTORY,
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         Environment.GetEnvironmentVariable(APPSERVICESAUTH_IDENTITYPROVIDER_ENVIRONMENTVARIABLE),
                         APPSERVICESAUTH_AAD,
                         StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}

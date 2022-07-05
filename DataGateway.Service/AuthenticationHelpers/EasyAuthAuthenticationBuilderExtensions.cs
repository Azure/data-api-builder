using System;
using Azure.DataGateway.Config;
using Microsoft.AspNetCore.Authentication;

namespace Azure.DataGateway.Service.AuthenticationHelpers
{
    /// <summary>
    /// Extension methods related to Static Web App/ App Service authentication (Easy Auth).
    /// This class allows setting up Easy Auth authentication in the startup class with
    /// a single call to .AddAuthentiction(scheme).AddStaticWebAppAuthentication()
    /// </summary>
    public static class EasyAuthAuthenticationBuilderExtensions
    {
        /// <summary>
        /// Add authentication with Static Web Apps.
        /// </summary>
        /// <param name="builder">Authentication builder.</param>
        /// <returns>The builder, to chain commands.</returns>
        public static AuthenticationBuilder AddEasyAuthAuthentication(
             this AuthenticationBuilder builder, string easyAuthAuthenticationProvider)
        {
            if (builder is null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }

            if (Enum.GetName(EasyAuthType.StaticWebApps)!.Equals(easyAuthAuthenticationProvider, StringComparison.OrdinalIgnoreCase))
            {
                builder.AddScheme<EasyAuthAuthenticationOptions, StaticWebAppsAuthenticationHandler>(
                    authenticationScheme: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME,
                    displayName: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME,
                    options => { });
            }

            if (Enum.GetName(EasyAuthType.AppService)!.Equals(easyAuthAuthenticationProvider, StringComparison.OrdinalIgnoreCase))
            {
                builder.AddScheme<EasyAuthAuthenticationOptions, AppServiceAuthenticationHandler>(
                    authenticationScheme: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME,
                    displayName: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME,
                    options => { });
            }

            return builder;
        }
    }
}

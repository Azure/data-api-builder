// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: EasyAuthAuthenticationBuilderExtensions.cs
// **************************************

using Azure.DataApiBuilder.Config;
using Microsoft.AspNetCore.Authentication;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
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
        /// <param name="easyAuthAuthenticationProvider">EasyAuth provider type. StaticWebApps or AppService</param>
        /// <returns>The builder, to chain commands.</returns>
        public static AuthenticationBuilder AddEasyAuthAuthentication(
             this AuthenticationBuilder builder, EasyAuthType easyAuthAuthenticationProvider)
        {
            if (builder is null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }

            builder.AddScheme<EasyAuthAuthenticationOptions, EasyAuthAuthenticationHandler>(
                authenticationScheme: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME,
                displayName: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME,
                options =>
                {
                    if (easyAuthAuthenticationProvider is EasyAuthType.StaticWebApps)
                    {
                        options.EasyAuthProvider = EasyAuthType.StaticWebApps;
                    }
                    else if (easyAuthAuthenticationProvider is EasyAuthType.AppService)
                    {
                        options.EasyAuthProvider = EasyAuthType.AppService;
                    }
                });
            return builder;
        }
    }
}

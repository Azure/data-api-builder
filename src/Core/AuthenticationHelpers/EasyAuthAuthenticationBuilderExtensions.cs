// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.Authentication;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// Extension methods related to Static Web App/ App Service authentication (Easy Auth).
/// This class allows setting up Easy Auth authentication in the startup class with
/// a single call to .AddAuthentication(scheme).AddStaticWebAppAuthentication()
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
            throw new ArgumentNullException(nameof(builder));
        }

        if (easyAuthAuthenticationProvider is EasyAuthType.StaticWebApps)
        {
            builder.AddScheme<EasyAuthAuthenticationOptions, EasyAuthAuthenticationHandler>(
                authenticationScheme: EasyAuthAuthenticationDefaults.SWAAUTHSCHEME,
                displayName: EasyAuthAuthenticationDefaults.SWAAUTHSCHEME,
                options =>
                {
                    options.EasyAuthProvider = EasyAuthType.StaticWebApps;
                });
        }
        else
        {
            builder.AddScheme<EasyAuthAuthenticationOptions, EasyAuthAuthenticationHandler>(
                authenticationScheme: EasyAuthAuthenticationDefaults.APPSERVICEAUTHSCHEME,
                displayName: EasyAuthAuthenticationDefaults.APPSERVICEAUTHSCHEME,
                options =>
                {
                    options.EasyAuthProvider = EasyAuthType.AppService;
                });
        }

        return builder;
    }

    /// <summary>
    /// Registers the StaticWebApps and AppService EasyAuth authentication schemes.
    /// Used for ConfigureAuthenticationV2() where all EasyAuth schemes are registered.
    /// This function doesn't register EasyAuthType.AppService if the AppService environment is not detected.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"></exception>
    public static AuthenticationBuilder AddEnvDetectedEasyAuth(this AuthenticationBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.AddScheme<EasyAuthAuthenticationOptions, EasyAuthAuthenticationHandler>(
           authenticationScheme: EasyAuthAuthenticationDefaults.SWAAUTHSCHEME,
           displayName: EasyAuthAuthenticationDefaults.SWAAUTHSCHEME,
           options =>
           {
               options.EasyAuthProvider = EasyAuthType.StaticWebApps;
           });

        bool appServiceEnvironmentDetected = AppServiceAuthenticationInfo.AreExpectedAppServiceEnvVarsPresent();

        if (appServiceEnvironmentDetected)
        {
            // Loggers not available at this point in startup.
            Console.WriteLine("AppService environment detected, configuring EasyAuth.AppService authentication scheme.");
            builder.AddScheme<EasyAuthAuthenticationOptions, EasyAuthAuthenticationHandler>(
                authenticationScheme: EasyAuthAuthenticationDefaults.APPSERVICEAUTHSCHEME,
                displayName: EasyAuthAuthenticationDefaults.APPSERVICEAUTHSCHEME,
                options =>
                {
                    options.EasyAuthProvider = EasyAuthType.AppService;
                });
        }

        return builder;
    }
}

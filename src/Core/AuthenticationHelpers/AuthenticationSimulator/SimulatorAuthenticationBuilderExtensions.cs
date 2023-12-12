// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Authentication;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers.AuthenticationSimulator;

/// <summary>
/// Extension methods related to Static Web App/ App Service authentication (Easy Auth).
/// This class allows setting up Easy Auth authentication in the startup class with
/// a single call to .AddAuthentiction(scheme).AddStaticWebAppAuthentication()
/// </summary>
public static class SimulatorAuthenticationBuilderExtensions
{
    /// <summary>
    /// Add authentication with Static Web Apps.
    /// </summary>
    /// <param name="builder">Authentication builder.</param>
    /// <param name="SimulatorAuthenticationProvider">Simulator provider type. StaticWebApps or AppService</param>
    /// <returns>The builder, to chain commands.</returns>
    public static AuthenticationBuilder AddSimulatorAuthentication(this AuthenticationBuilder builder)
    {
        if (builder is null)
        {
            throw new System.ArgumentNullException(nameof(builder));
        }

        builder.AddScheme<AuthenticationSchemeOptions, SimulatorAuthenticationHandler>(
            authenticationScheme: SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME,
            displayName: SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME,
            configureOptions: null);

        return builder;
    }
}

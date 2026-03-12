// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Authentication;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers.UnauthenticatedAuthentication;

/// <summary>
/// Extension methods related to Unauthenticated authentication.
/// This class allows setting up Unauthenticated authentication in the startup class with
/// a single call to .AddAuthentication(scheme).AddUnauthenticatedAuthentication()
/// </summary>
public static class UnauthenticatedAuthenticationBuilderExtensions
{
    /// <summary>
    /// Add authentication with Unauthenticated provider.
    /// </summary>
    /// <param name="builder">Authentication builder.</param>
    /// <returns>The builder, to chain commands.</returns>
    public static AuthenticationBuilder AddUnauthenticatedAuthentication(this AuthenticationBuilder builder)
    {
        if (builder is null)
        {
            throw new System.ArgumentNullException(nameof(builder));
        }

        builder.AddScheme<AuthenticationSchemeOptions, UnauthenticatedAuthenticationHandler>(
            authenticationScheme: UnauthenticatedAuthenticationDefaults.AUTHENTICATIONSCHEME,
            displayName: UnauthenticatedAuthenticationDefaults.AUTHENTICATIONSCHEME,
            configureOptions: null);

        return builder;
    }
}

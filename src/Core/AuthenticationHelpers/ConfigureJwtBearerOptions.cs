// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Validators;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;

namespace Azure.DataApiBuilder.Service;

/// <summary>
/// Named options configuration for JwtBearerOptions.
/// </summary>
public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly RuntimeConfigProvider _runtimeConfigProvider;

    /// <summary>
    /// By registering this instance of IConfigureNamedOptions<JwtBearerOptions>, the internal
    /// .NET OptionsFactory will call Configure(string? name, JwtBearerOptions options)
    /// when JwtBearerOptions is requested and fetch the latest configuration
    /// from the RuntimeConfigProvider.
    /// </summary>
    /// <param name="runtimeConfigProvider">Source of latest configuration.</param>
    public ConfigureJwtBearerOptions(RuntimeConfigProvider runtimeConfigProvider)
    {
        _runtimeConfigProvider = runtimeConfigProvider;
    }

    // JwtBearerOptions configuration. Returned to OptionsFactory.
    public void Configure(string? name, JwtBearerOptions options)
    {
        // Don't refresh authentication config when hot reload is disabled.
        if (!_runtimeConfigProvider.IsConfigHotReloadable())
        {
            return;
        }

        AuthenticationOptions? newAuthOptions = _runtimeConfigProvider.GetConfig().Runtime?.Host?.Authentication;

        // Don't configure JwtBearerOptions when JWT properties(issuer/audience) are excluded.
        if (newAuthOptions is null || newAuthOptions.Jwt is null)
        {
            return;
        }

        // Only configure the options if this is the correct instance
        options.MapInboundClaims = false;
        options.Audience = newAuthOptions.Jwt.Audience;
        options.Authority = newAuthOptions.Jwt.Issuer;

        string? jwksUri = newAuthOptions.Jwt.ResolvedJwksUrl;
        if (string.IsNullOrWhiteSpace(jwksUri))
        {
            return;
        }

        JsonWebKeySet jwks;

        using (HttpClient client = JwtHttpClientFactory.Create())
        {
            string jwksJson = client.GetStringAsync(jwksUri).GetAwaiter().GetResult();
            jwks = new JsonWebKeySet(jwksJson);
        }

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
        {
            ValidAudience = newAuthOptions.Jwt.Audience,
            ValidIssuer = newAuthOptions.Jwt.Issuer,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = jwks.Keys,
            // Instructs the asp.net core middleware which JWT claim to use for User.IsInRole()
            // Defaults to "roles" when not explicitly configured.
            RoleClaimType = newAuthOptions.Jwt.ResolvedRoleClaimType
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                JwtRoleClaimsTransformer.NormalizeRoleClaims(
                    principal: context.Principal!,
                    sourceRoleClaimType: newAuthOptions.Jwt.ResolvedRoleClaimType,
                    separator: newAuthOptions.Jwt.ResolvedRolesSeparator);

                return Task.CompletedTask;
            }
        };

        if (newAuthOptions.Provider.Equals("AzureAD") || newAuthOptions.Provider.Equals("EntraID"))
        {
            // Enables the validation of the issuer of the signing keys
            // used by the Microsoft identity platform (AAD) against the issuer of the token.
            options.TokenValidationParameters.EnableAadSigningKeyIssuerValidation();
        }
    }

    // This won't be called, but is required for the interface
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}

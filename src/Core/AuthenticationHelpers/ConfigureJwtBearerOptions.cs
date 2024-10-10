// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Validators;

namespace Azure.DataApiBuilder.Service;

public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly RuntimeConfigProvider _runtimeConfigProvider;
    public ConfigureJwtBearerOptions(RuntimeConfigProvider runtimeConfigProvider)
    {
        _runtimeConfigProvider = runtimeConfigProvider;
    }

    // Configure the named instance
    public void Configure(string? name, JwtBearerOptions options)
    {
        // Skip JwtBearerOptions hot-reload circumstances in ProdMode. 
        if (!_runtimeConfigProvider.IsConfigHotReloadable())
        {
            // perhaps have this check in the change token ? 
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
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
        {
            ValidAudience = newAuthOptions.Jwt.Audience,
            ValidIssuer = newAuthOptions.Jwt.Issuer,
            // Instructs the asp.net core middleware to use the data in the "roles" claim for User.IsInRole()
            // See https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.isinrole?view=net-6.0#remarks
            RoleClaimType = AuthenticationOptions.ROLE_CLAIM_TYPE
        };

        if (newAuthOptions.Provider.Equals("AzureAD") || newAuthOptions.Provider.Equals("EntraID"))
        {
            options.TokenValidationParameters.EnableAadSigningKeyIssuerValidation();
        }
    }

    // This won't be called, but is required for the interface
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}

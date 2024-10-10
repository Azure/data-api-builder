// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Validators;
using static NodaTime.TimeZones.ZoneEqualityComparer;

namespace Azure.DataApiBuilder.Core.Configurations;

public class JwtConfigChangeRelay
{
    private readonly List<IDisposable> _registrations = new();
    private readonly IOptionsMonitor<JwtBearerOptions> _bearerOptionsMonitor;
    private readonly IOptionsMonitorCache<JwtBearerOptions> _bearerOptionsMonitorCache;
    private readonly IPostConfigureOptions<JwtBearerOptions>[] _postConfigures;
    private readonly RuntimeConfigProvider  _runtimeConfigProvider;
    private readonly List<IDisposable> _changeTokenRegistrations;

    public JwtConfigChangeRelay(
        IEnumerable<IOptionsChangeTokenSource<JwtBearerOptions>> sources,
        IOptionsMonitor<JwtBearerOptions> bearerOptionsMonitor,
        IOptionsMonitorCache<JwtBearerOptions> jwtOptionsMonitorCache,
        IEnumerable<IPostConfigureOptions<JwtBearerOptions>> jwtPostConfigureOptions,
        RuntimeConfigProvider runtimeConfigProvider)
    {
        _bearerOptionsMonitor = bearerOptionsMonitor;
        _bearerOptionsMonitorCache = jwtOptionsMonitorCache;
        _postConfigures = jwtPostConfigureOptions as IPostConfigureOptions<JwtBearerOptions>[] ?? new List<IPostConfigureOptions<JwtBearerOptions>>(jwtPostConfigureOptions).ToArray();
        _runtimeConfigProvider = runtimeConfigProvider;

        _changeTokenRegistrations = new List<IDisposable>(1)
        {
            ChangeToken.OnChange(_runtimeConfigProvider.GetChangeToken,PostChangeTokenChangedAction, false)
        };
    }

    //https://github.com/dotnet/aspnetcore/issues/52296
    public void ConfigureJwtBearerProvider()
    {
        AuthenticationOptions? newAuthOptions = _runtimeConfigProvider.GetConfig().Runtime?.Host?.Authentication;

        JwtBearerOptions jwtOptions = new()
        {
            MapInboundClaims = false,
            Audience = newAuthOptions?.Jwt?.Audience,
            Authority = newAuthOptions?.Jwt?.Issuer,
            TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
            {
                ValidAudience = newAuthOptions?.Jwt?.Audience,
                ValidIssuers = new List<string>() { "https://login.microsoftonline.com/291bf275-ea78-4cde-84ea-21309a43a567/v2.0" },
                // Instructs the asp.net core middleware to use the data in the "roles" claim for User.IsInRole()
                // See https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.isinrole?view=net-6.0#remarks
                RoleClaimType = AuthenticationOptions.ROLE_CLAIM_TYPE
            } 
        };

        //jwtOptions.TokenValidationParameters.EnableAadSigningKeyIssuerValidation();
        //jwtOptions.TokenValidationParameters.SignatureValidator = (token, _) => new JsonWebToken(token);
        _bearerOptionsMonitorCache.TryRemove(JwtBearerDefaults.AuthenticationScheme);
        foreach (IPostConfigureOptions<JwtBearerOptions> post in _postConfigures)
        {
            post.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions);
        }
       // _jwtPostConfigureOptions.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions);
        _bearerOptionsMonitorCache.GetOrAdd(JwtBearerDefaults.AuthenticationScheme, () => jwtOptions);
        Console.WriteLine("New Audience: " + _bearerOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme).Audience);
        Console.WriteLine("New Issuer: " + _bearerOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme).Authority);

    }

    private void PostChangeTokenChangedAction(bool forceUpdate = false)
    {
        // Skip hot reload circumstances in ProdMode. 
        if (!forceUpdate && (_runtimeConfigProvider.IsLateConfigured || _runtimeConfigProvider.GetConfig().Runtime?.Host?.Mode == HostMode.Production))
        {
            return;
        }
        //Console.WriteLine("Old Audience: " + jwtOptions.Audience);
        JwtBearerOptions jwtOptions = _bearerOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);
        //Console.WriteLine("Current options: " + jwtOptions.Audience);
        AuthenticationOptions? newAuthOptions = _runtimeConfigProvider.GetConfig().Runtime?.Host?.Authentication;

        if (_bearerOptionsMonitorCache.TryRemove(JwtBearerDefaults.AuthenticationScheme))
        {
            jwtOptions.Audience = newAuthOptions?.Jwt?.Audience;
            jwtOptions.TokenValidationParameters.ValidAudience = newAuthOptions?.Jwt?.Audience;
            jwtOptions.Authority = newAuthOptions?.Jwt?.Issuer;
           // _jwtPostConfigureOptions.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions);
            _bearerOptionsMonitorCache.GetOrAdd(JwtBearerDefaults.AuthenticationScheme, () => jwtOptions);
            Console.WriteLine("New Audience: " + _bearerOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme).Audience);
        }
    }
}

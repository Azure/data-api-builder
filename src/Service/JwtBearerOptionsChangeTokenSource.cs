// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service;

/// <summary>
/// Used by IOptionsMonitor<JwtBearerOptions> to register a change token for JwtBearerOptions.
/// When DAB gets a new runtimeconfig via hot-reload, DAB will signal
/// (via RuntimeConfigLoader -> RuntimeConfigProvider -> JwtBearerOptionsChangeTokenSource)
/// that a change has occurred and IOptionsMonitor will reload the JwtBearerOptions.
/// </summary>
/// <seealso cref="https://github.com/dotnet/aspnetcore/issues/49586#issuecomment-1671838595"/>
public class JwtBearerOptionsChangeTokenSource : IOptionsChangeTokenSource<JwtBearerOptions>
{
    private readonly RuntimeConfigProvider _configProvider;

    /// <summary>
    /// Get RuntimeConfigProvider to use as the change token source.
    /// </summary>
    /// <param name="configProvider">Change token source.</param>
    public JwtBearerOptionsChangeTokenSource(RuntimeConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    public string Name => "Bearer";

    /// <summary>
    /// Returns a change token that signals when the JwtBearerOptions should be reloaded.
    /// Used by ChangeToken.OnChange to register a callback when the change token signals.
    /// </summary>
    /// <seealso cref="https://learn.microsoft.com/aspnet/core/fundamentals/change-tokens#simple-startup-change-token"/>
    /// <returns>DabChangeToken</returns>
    public IChangeToken GetChangeToken()
    {
        return _configProvider.GetChangeToken();
    }
}


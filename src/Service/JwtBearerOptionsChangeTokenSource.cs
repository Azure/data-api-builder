// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service;

public class JwtBearerOptionsChangeTokenSource : IOptionsChangeTokenSource<JwtBearerOptions>
{
    //private readonly DabChangeToken _changeToken;
    private readonly RuntimeConfigProvider _configProvider;

    public JwtBearerOptionsChangeTokenSource(RuntimeConfigProvider configProvider)
    {
        // Change event source is the provider.
        _configProvider = configProvider;
    }

    public string Name => "Bearer";

    public IChangeToken GetChangeToken()
    {
        return _configProvider.GetChangeToken();
    }
}


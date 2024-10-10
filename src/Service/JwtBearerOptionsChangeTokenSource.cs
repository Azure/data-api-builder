// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service;

public class JwtBearerOptionsChangeTokenSource : IOptionsChangeTokenSource<JwtBearerOptions>
{
    //private readonly DabChangeToken _changeToken;
    private readonly RuntimeConfigLoader _configLoader;

    public JwtBearerOptionsChangeTokenSource(RuntimeConfigLoader configLoader)
    {
        // Change event source is the provider.
        _configLoader = configLoader;
    }

    public string Name => "DogToken";

    public IChangeToken GetChangeToken()
    {
        return _configLoader.GetChangeToken();
    }
}


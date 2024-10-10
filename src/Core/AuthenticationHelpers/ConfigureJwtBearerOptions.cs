// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Azure.DataApiBuilder.Service;

public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    public ConfigureJwtBearerOptions()
    {
    }

    // Configure the named instance
    public void Configure(string? name, JwtBearerOptions options)
    {
        // Only configure the options if this is the correct instance
        Console.WriteLine("HEY THERE ! Configuring JwtBearerOptions for: " + name);
        options.Audience = "New AUdience?";
    }

    // This won't be called, but is required for the interface
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}

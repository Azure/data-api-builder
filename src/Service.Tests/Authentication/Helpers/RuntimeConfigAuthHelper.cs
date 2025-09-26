// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Service.Tests.Authentication.Helpers;

internal static class RuntimeConfigAuthHelper
{
    internal static RuntimeConfig CreateTestConfigWithAuthNProvider(AuthenticationOptions authenticationOptions)
    {
        DataSource dataSource = new(DatabaseType.MSSQL, "", new());

        Config.ObjectModel.HostOptions hostOptions = new(Cors: null, Authentication: authenticationOptions);
        RuntimeConfig config = new(
            Schema: FileSystemRuntimeConfigLoader.SCHEMA,
            DataSource: dataSource,
            Runtime: new(
                Rest: new(),
                GraphQL: new(),
                Mcp: new(),
                Host: hostOptions
            ),
            Entities: new(new Dictionary<string, Entity>())
        );
        return config;
    }
}

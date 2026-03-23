// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

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

    internal static RuntimeConfig CreateTestConfigWithAuthNProviderAndUserDelegatedAuth(
        AuthenticationOptions authenticationOptions,
        UserDelegatedAuthOptions userDelegatedAuthOptions)
    {
        DataSource dataSource = new DataSource(DatabaseType.MSSQL, "", new Dictionary<string, object?>()) with
        {
            UserDelegatedAuth = userDelegatedAuthOptions
        };

        HostOptions hostOptions = new(Cors: null, Authentication: authenticationOptions);
        RuntimeConfig config = new(
            Schema: FileSystemRuntimeConfigLoader.SCHEMA,
            DataSource: dataSource,
            Runtime: new RuntimeOptions(
                Rest: new RestRuntimeOptions(),
                GraphQL: new GraphQLRuntimeOptions(),
                Mcp: new McpRuntimeOptions(),
                Host: hostOptions
            ),
            Entities: new(new Dictionary<string, Entity>())
        );

        return config;
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Cli.Tests;

static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.ConnectionString);
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.Schema);
    }
}

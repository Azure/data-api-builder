// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config;
using VerifyTests;

namespace Azure.DataApiBuilder.Service.Tests;

static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.ConnectionString);
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.Schema);
        VerifyDiffPlex.Initialize();
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config;
using VerifyMSTest;
using VerifyTests;

namespace Azure.DataApiBuilder.Service.Tests;

static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.ConnectionString);
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.Schema);
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.DatabaseTypeNotSupportedMessage);
        VerifyBase.DerivePathInfo(
            (sourceFile, projectDirectory, type, method) => new(
                directory: Path.Combine(projectDirectory, "Snapshots"),
                typeName: type.Name,
                methodName: method.Name));
        VerifyDiffPlex.Initialize();
    }
}

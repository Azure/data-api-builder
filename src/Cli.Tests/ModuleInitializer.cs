// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Cli.Tests;

/// <summary>
/// Setup global settings for the test project.
/// </summary>
static class ModuleInitializer
{
    /// <summary>
    /// Initialize the Verifier settings we used for the project, such as what fields to ignore
    /// when comparing objects and how we will name the snapshot files.
    /// </summary>
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

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.ObjectModel;
using VerifyMSTest;
using VerifyTests;

namespace Azure.DataApiBuilder.Service.Tests;

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
        // Ignore the connection string from the output to avoid committing it.
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.ConnectionString);
        // Ignore the JSON schema path as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.Schema);
        // Ignore the databaseNameToDataSource array as its not exposed to customer.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.DataSourceNameToDataSource);
        // Ignore the EntityNameToDataSourceName array as its not exposed to customer.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.EntityNameToDataSourceName);
        // Ignore the DefaultDataSourceName array as its not exposed to customer.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.DefaultDataSourceName);
        // Ignore the message as that's not serialized in our config file anyway.
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.DatabaseTypeNotSupportedMessage);
        // Customise the path where we store snapshots, so they are easier to locate in a PR review.
        VerifyBase.DerivePathInfo(
            (sourceFile, projectDirectory, type, method) => new(
                directory: Path.Combine(projectDirectory, "Snapshots"),
                typeName: type.Name,
                methodName: method.Name));
        // Enable DiffPlex output to better identify in the test output where the failure is with a rich diff.
        VerifyDiffPlex.Initialize();
    }
}

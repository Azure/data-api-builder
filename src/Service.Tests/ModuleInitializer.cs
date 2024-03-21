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
        // Ignore the datasource files as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.DataSourceFiles);
        // Ignore the SqlDataSourceUsed as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.SqlDataSourceUsed);
        // Ignore the global IsCachingEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsCachingEnabled);
        // Ignore the global RuntimeOptions.IsCachingEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeOptions>(options => options.IsCachingEnabled);
        // Ignore the entity IsCachingEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<Entity>(entity => entity.IsCachingEnabled);
        // Ignore the UserProvidedTtlOptions. They aren't serialized to our config file, enforced by EntityCacheOptionsConverter.
        VerifierSettings.IgnoreMember<EntityCacheOptions>(cacheOptions => cacheOptions.UserProvidedTtlOptions);
        // Ignore the CosmosDataSourceUsed as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.CosmosDataSourceUsed);
        // Ignore the IsRequestBodyStrict as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsRequestBodyStrict);
        // Ignore the IsGraphQLEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsGraphQLEnabled);
        // Ignore the IsRestEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsRestEnabled);
        // Ignore the IsStaticWebAppsIdentityProvider as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsStaticWebAppsIdentityProvider);
        // Ignore the RestPath as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.RestPath);
        // Ignore the GraphQLPath as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.GraphQLPath);
        // Ignore the AllowIntrospection as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.AllowIntrospection);
        // Ignore the message as that's not serialized in our config file anyway.
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.DatabaseTypeNotSupportedMessage);
        // Ignore DefaultDataSourceName as that's not serialized in our config file.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.DefaultDataSourceName);
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

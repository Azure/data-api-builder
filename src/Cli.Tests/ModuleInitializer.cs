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
        // Ignore the connection string from the output to avoid committing it.
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.ConnectionString);
        // Ignore the IsDatasourceHealthEnabled from the output to avoid committing it.
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.IsDatasourceHealthEnabled);
        // Ignore the DatasourceThresholdMs from the output to avoid committing it.
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.DatasourceThresholdMs);
        // Ignore the datasource files as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.DataSourceFiles);
        // Ignore the CosmosDataSourceUsed as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.CosmosDataSourceUsed);
        // Ignore the SqlDataSourceUsed as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.SqlDataSourceUsed);
        // Ignore the global IsCachingEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsCachingEnabled);
        // Ignore the global RuntimeOptions.IsCachingEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeOptions>(options => options.IsCachingEnabled);
        // Ignore the entity IsCachingEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<Entity>(entity => entity.IsCachingEnabled);
        // Ignore the global IsRestEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsRestEnabled);
        // Ignore the global RuntimeOptions.IsRestEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeOptions>(options => options.IsRestEnabled);
        // Ignore the entity IsRestEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<Entity>(entity => entity.IsRestEnabled);
        // Ignore the global IsGraphQLEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsGraphQLEnabled);
        // Ignore the global RuntimeOptions.IsGraphQLEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeOptions>(options => options.IsGraphQLEnabled);
        // Ignore the entity IsGraphQLEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<Entity>(entity => entity.IsGraphQLEnabled);
        // Ignore the global IsHealthEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.IsHealthEnabled);
        // Ignore the global RuntimeOptions.IsHealthCheckEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeOptions>(options => options.IsHealthCheckEnabled);
        // Ignore the entity IsEntityHealthEnabled as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<Entity>(entity => entity.IsEntityHealthEnabled);
        // Ignore the entity EntityThresholdMs as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<Entity>(entity => entity.EntityThresholdMs);
        // Ignore the entity EntityFirst as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<Entity>(entity => entity.EntityFirst);
        // Ignore the entity IsLinkingEntity as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<Entity>(entity => entity.IsLinkingEntity);
        // Ignore the UserProvidedTtlOptions. They aren't serialized to our config file, enforced by EntityCacheOptionsConverter.
        VerifierSettings.IgnoreMember<EntityCacheOptions>(cacheOptions => cacheOptions.UserProvidedTtlOptions);
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
        // Ignore the EnableAggregation as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(options => options.EnableAggregation);
        // Ignore the AllowedRolesForHealth as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.AllowedRolesForHealth);
        // Ignore the CacheTtlSeconds as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.CacheTtlSecondsForHealthReport);
        // Ignore the EnableAggregation as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<GraphQLRuntimeOptions>(options => options.EnableAggregation);
        // Ignore the EnableDwNto1JoinOpt as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(options => options.EnableDwNto1JoinOpt);
        // Ignore the FeatureFlags as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<GraphQLRuntimeOptions>(options => options.FeatureFlags);
        // Ignore the JSON schema path as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.Schema);
        // Ignore the message as that's not serialized in our config file anyway.
        VerifierSettings.IgnoreMember<DataSource>(dataSource => dataSource.DatabaseTypeNotSupportedMessage);
        // Ignore DefaultDataSourceName as that's not serialized in our config file.
        VerifierSettings.IgnoreMember<RuntimeConfig>(config => config.DefaultDataSourceName);
        // Ignore MaxResponseSizeMB as as that's unimportant from a test standpoint.
        VerifierSettings.IgnoreMember<HostOptions>(options => options.MaxResponseSizeMB);
        // Ignore UserProvidedMaxResponseSizeMB as that's not serialized in our config file.
        VerifierSettings.IgnoreMember<HostOptions>(options => options.UserProvidedMaxResponseSizeMB);
        // Ignore UserProvidedDepthLimit as that's not serialized in our config file.
        VerifierSettings.IgnoreMember<GraphQLRuntimeOptions>(options => options.UserProvidedDepthLimit);
        // Ignore EnableLegacyDateTimeScalar as that's not serialized in our config file.
        VerifierSettings.IgnoreMember<GraphQLRuntimeOptions>(options => options.EnableLegacyDateTimeScalar);
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

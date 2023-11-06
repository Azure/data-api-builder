// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#nullable enable
using System.Linq;
using System.Text;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Caching;

/// <summary>
/// Validates that the caching configuration in the runtime config is deserialized correctly.
/// </summary>
[TestClass]
public class CachingConfigDeserializationTests
{
    private const int DEFAULT_CACHE_TTL_SECONDS = 60;
    private const string DAB_DRAFT_SCHEMA_TEST_PATH = "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json";

    /// <summary>
    /// Validates that RuntimeConfigLoader.TryParseConfig(string jsonConfig) successfully deserializes
    /// the EntityCacheOptions defined on an entity.
    /// ttl-seconds will always resolve to either the user defined value or the default value.
    /// </summary>
    /// <param name="entityCacheConfig">Escaped JSON string defining entity cache configuration.</param>
    /// <param name="expectedEnabled">Whether to expect deserialized EntityCacheOptions to be enabled.</param>
    /// <param name="expectedTTL">Expected ttl value resolved during deserialization.</param>
    /// <param name="expectedUserDefinedTtl">Whether resolved EntityCacheOptions recognizes that ttl was set by user.</param>
    [DataRow(@"", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions left out of JSON config: default values used.")]
    [DataRow(@",""cache"": null", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions set to Null: default values used.")]
    [DataRow(@",""cache"": {}", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions is empty: default values used.")]
    [DataRow(@",""cache"": { ""enabled"": false }", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions disabled with left out ttl: -> Provided Enabled flag used with default ttl")]
    [DataRow(@",""cache"": { ""enabled"": false, ""ttl-seconds"": 2147483647 }", false, 2147483647, true, DisplayName = "EntityCacheOptions disabled with explicit Int.MaxValue ttl: -> provided values used and userDefined flag set to true")]
    [DataRow(@",""cache"": { ""enabled"": true }", true, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions enabled and ttl left out: provided enabled flag used with default ttl and userdefined flag set to false")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 60 }", true, DEFAULT_CACHE_TTL_SECONDS, true, DisplayName = "EntityCacheOptions provided: provided values used and userdefined flag set to true")]
    [DataRow(@",""cache"": { ""enabled"": null }", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions.Enabled set to null results in (default) disabled entity cache.")]
    [DataRow(@",""cache"": { ""enabled"": false, ""ttl-seconds"": null }", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions.TtlSeconds set to null results in (default) ttl value.")]
    [DataTestMethod]
    public void EntityCacheOptionsDeserialization_ValidJson(
    string entityCacheConfig,
    bool expectedEnabled,
    int expectedTTL,
        bool expectedUserDefinedTtl)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig: string.Empty, entityCacheConfig: entityCacheConfig);

        // Act
        RuntimeConfigLoader.TryParseConfig(
            json: fullConfig,
            out RuntimeConfig? config,
            logger: null,
            connectionString: null,
            replaceEnvVar: false,
            dataSourceName: string.Empty,
            datasourceNameToConnectionString: null,
            replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsNotNull(config, message: "Config must not be null, runtime config JSON deserialization failed.");

        Entity entity = config.Entities.First().Value;
        Assert.AreEqual(expectedEnabled, entity.IsCachingEnabled, message: "EntityCacheConfig.Enabled expected to be: " + expectedEnabled);

        EntityCacheOptions? resolvedEntityCacheOptions = entity.Cache;
        if (expectedEnabled)
        {
            Assert.IsNotNull(resolvedEntityCacheOptions, message: "EntityCacheConfig must not be null, unexpected entity JSON deserialization result.");
            Assert.AreEqual(expectedEnabled, resolvedEntityCacheOptions.Enabled, message: "EntityCacheConfig.Enabled expected to be: " + expectedEnabled);
            Assert.AreEqual(expectedTTL, resolvedEntityCacheOptions.TtlSeconds);
            Assert.AreEqual(expectedUserDefinedTtl, resolvedEntityCacheOptions.UserProvidedTtlOptions, message: "UserProvidedTtlOptions expected to be: " + expectedUserDefinedTtl);
        }
    }

    /// <summary>
    /// Validates that unexpected values provided for EntityCacheOptions in the JSON config result in
    /// a failure to deserialize the runtime config.
    /// </summary>
    /// <param name="entityCacheConfig">Escaped JSON string defining entity cache configuration.</param>
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 2147483648 }", DisplayName = "EntityCacheOptions.TtlSeconds set to Int.MaxValue+1 is invalid for parser.")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": -2147483649 }", DisplayName = "EntityCacheOptions.TtlSeconds set to Int.MinValue-1 is invalid for parser.")]
    [DataTestMethod]
    public void EntityCacheOptionsDeserialization_InvalidJson(string entityCacheConfig)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig: string.Empty, entityCacheConfig: entityCacheConfig);

        // Act
        bool parsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            json: fullConfig,
            out _,
            logger: null,
            connectionString: null,
            replaceEnvVar: false,
            dataSourceName: string.Empty,
            datasourceNameToConnectionString: null,
            replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsFalse(parsingSuccessful, message: "Expected JSON parsing to fail.");
    }

    /// <summary>
    /// Validates that RuntimeConfigLoader.TryParseConfig(string jsonConfig) successfully deserializes
    /// the global Runtime.Cache property.
    /// </summary>
    /// <param name="globalCacheConfig">Escaped JSON string defining global cache configuration.</param>
    /// <param name="expectedEnabled">Whether to expect deserialized Runtime.Cache to be enabled.</param>
    [DataRow(@"", false, DisplayName = "Global cache property left out of JSON config: default values used.")]
    [DataRow(@",""cache"": null", false, DisplayName = "Global cache property set to Null: default values used.")]
    [DataRow(@",""cache"": false", false, DisplayName = "Global cache property disabled -> Provided Enabled flag used with default ttl")]
    [DataRow(@",""cache"": true", true, DisplayName = "EntityGlobal cache property enabled -> provided values used and userDefined flag set to true")]
    [DataTestMethod]
    public void GlobalCacheOptionsDeserialization_ValidJson(
        string globalCacheConfig,
        bool expectedEnabled)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig: globalCacheConfig, entityCacheConfig: string.Empty);

        // Act
        RuntimeConfigLoader.TryParseConfig(
            json: fullConfig,
            out RuntimeConfig? config,
            logger: null,
            connectionString: null,
            replaceEnvVar: false,
            dataSourceName: string.Empty,
            datasourceNameToConnectionString: null,
            replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsNotNull(config, message: "Config must not be null, runtime config JSON deserialization failed.");
        Assert.AreEqual(expected: expectedEnabled, actual: config.IsCachingEnabled, message: "RuntimeConfig.CacheEnabled expected to be: " + expectedEnabled);

        if (expectedEnabled)
        {
            Assert.IsNotNull(config.Runtime?.CacheEnabled, message: "Expected global cache property to be non-null.");
        }
    }

    /// <summary>
    /// Validates that unexpected values provided for global runtime.cache property in the JSON config result in
    /// a failure to deserialize the runtime config.
    /// </summary>
    /// <param name="globalCacheConfig">Escaped JSON string defining entity cache configuration.</param>
    [DataRow(@",""cache"": {}", DisplayName = "Global cache property is empty: deserialization fails.")]
    [DataTestMethod]
    public void GlobalCacheOptionsDeserialization_InvalidJson(string entityCacheConfig)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig: entityCacheConfig, entityCacheConfig: string.Empty);

        // Act
        bool parsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            json: fullConfig,
            out _,
            logger: null,
            connectionString: null,
            replaceEnvVar: false,
            dataSourceName: string.Empty,
            datasourceNameToConnectionString: null,
            replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsFalse(parsingSuccessful, message: "Expected JSON parsing to fail.");
    }

    /// <summary>
    /// Returns a JSON string of the runtime config with the test-provided
    /// cache configuration.
    /// </summary>
    /// <param name="globalCacheConfig">Escaped JSON string defining global cache configuration.</param>
    /// <param name="entityCacheConfig">Escaped JSON string defining entity cache configuration.</param>
    /// <returns></returns>
    private static string GetRawConfigJson(string globalCacheConfig, string entityCacheConfig)
    {
        StringBuilder expectedRuntimeConfigJson = new(
        @"{" +
            @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
            @"""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""ReplaceMe"",
                    ""options"":{
                        ""set-session-context"": false
                    }
                },
           ""runtime"": {
                ""rest"": {
                  ""enabled"": true,
                  ""path"": ""/api"",
                  ""request-body-strict"": true
                  },
                ""graphql"": {
                  ""enabled"": true,
                  ""path"": ""/An_"",
                  ""allow-introspection"": true
                  },
                ""host"": {
                  ""cors"": {
                    ""origins"": [],
                    ""allow-credentials"": false
                        },
                  ""authentication"": {
                    ""provider"": ""StaticWebApps""
                        },
                  ""mode"": ""production""
                  }" + globalCacheConfig +
              @"},
              ""entities"": {
                ""Book"": {
                  ""source"": {
                    ""object"": ""books"",
                    ""type"": ""table""
                  },
                  ""graphql"": {
                    ""enabled"": true,
                    ""type"": {
                      ""singular"": ""book"",
                      ""plural"": ""books""
                    }
                  },
                  ""rest"": {
                    ""enabled"": true
                  },
                  ""permissions"": [
                    {
                        ""role"": ""anonymous"",
                        ""actions"": [ ""*"" ]
                    }]" + entityCacheConfig + @"
                    }
                }
            }");

        expectedRuntimeConfigJson = expectedRuntimeConfigJson.Replace(" ", string.Empty);
        expectedRuntimeConfigJson = expectedRuntimeConfigJson.Replace("\r\n", string.Empty);

        return expectedRuntimeConfigJson.ToString();
    }
}

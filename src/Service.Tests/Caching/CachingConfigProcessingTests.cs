// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#nullable enable
using System.Linq;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Caching;

/// <summary>
/// Validates that the caching configuration in the runtime config is deserialized correctly.
/// </summary>
[TestClass]
public class CachingConfigProcessingTests
{
    /// <summary>
    /// Default ttl value for an entity. Must align with EntityCacheOptions.DEFAULT_TTL_SECONDS.
    /// </summary>
    private const int DEFAULT_CACHE_TTL_SECONDS = 5;

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
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 5 }", true, DEFAULT_CACHE_TTL_SECONDS, true, DisplayName = "EntityCacheOptions provided: provided values used and userdefined flag set to true")]
    [DataRow(@",""cache"": { ""enabled"": null }", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions.Enabled set to null results in (default) disabled entity cache.")]
    [DataRow(@",""cache"": { ""enabled"": false, ""ttl-seconds"": null }", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "EntityCacheOptions.TtlSeconds set to null results in (default) ttl value.")]
    [DataTestMethod]
    public void EntityCacheOptionsDeserialization_ValidJson(
        string entityCacheConfig,
        bool expectCacheEnabled,
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
            replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsNotNull(config, message: "Config must not be null, runtime config JSON deserialization failed.");

        Entity entity = config.Entities.First().Value;
        Assert.AreEqual(expectCacheEnabled, entity.IsCachingEnabled, message: "EntityCacheConfig.Enabled expected to be: " + expectCacheEnabled);

        EntityCacheOptions? resolvedEntityCacheOptions = entity.Cache;
        if (expectCacheEnabled)
        {
            Assert.IsNotNull(resolvedEntityCacheOptions, message: "EntityCacheConfig must not be null, unexpected entity JSON deserialization result.");
            Assert.AreEqual(expectCacheEnabled, resolvedEntityCacheOptions.Enabled, message: "EntityCacheConfig.Enabled expected to be: " + expectCacheEnabled);
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
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 0 }", DisplayName = "EntityCacheOptions.TtlSeconds set to zero is invalid configuration.")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": -1 }", DisplayName = "EntityCacheOptions.TtlSeconds set to negative number is invalid configuration.")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 1.1 }", DisplayName = "EntityCacheOptions.TtlSeconds set to decimal is invalid configuration.")]
    [DataRow(@",""cache"": { ""enabled"": 1 }", DisplayName = "EntityCacheOptions.Enabled property set to 1 should fail because not a boolean.")]
    [DataRow(@",""cache"": { ""enabled"": 0 }", DisplayName = "EntityCacheOptions.Enabled property set to 0 should fail because not a boolean.")]
    [DataRow(@",""cache"": 1", DisplayName = "EntityCacheOptions property set to 1 should fail because it's not a JSON object.")]
    [DataRow(@",""cache"": 0", DisplayName = "EntityCacheOptions property set to 0 should fail because it's not a JSON object.")]
    [DataRow(@",""cache"": true", DisplayName = "EntityCacheOptions property set to true should fail because it's not a JSON object.")]
    [DataRow(@",""cache"": false", DisplayName = "EntityCacheOptions property set to false should fail because it's not a JSON object.")]
    [DataTestMethod]
    public void EntityCacheOptionsDeserialization_InvalidValues(string entityCacheConfig)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig: string.Empty, entityCacheConfig: entityCacheConfig);

        // Act
        bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            json: fullConfig,
            out _,
            logger: null,
            connectionString: null,
            replaceEnvVar: false,
            replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsFalse(isParsingSuccessful, message: "Expected JSON parsing to fail.");
    }

    /// <summary>
    /// Validates that RuntimeConfigLoader.TryParseConfig(string jsonConfig) successfully deserializes
    /// the global Runtime.Cache property.
    /// </summary>
    /// <param name="globalCacheConfig">Escaped JSON string defining global cache configuration.</param>
    /// <param name="expectCacheEnabled">Whether to expect deserialized Runtime.Cache to be enabled.</param>
    [DataRow(@"", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "GlobalCacheOptions left out of JSON config: default values used.")]
    [DataRow(@",""cache"": null", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "GlobalCacheOptions set to Null: default values used.")]
    [DataRow(@",""cache"": {}", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "GlobalCacheOptions is empty: default values used.")]
    [DataRow(@",""cache"": { ""enabled"": false }", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "GlobalCacheOptions disabled with left out ttl: -> Provided Enabled flag used with default ttl")]
    [DataRow(@",""cache"": { ""enabled"": false, ""ttl-seconds"": 2147483647 }", false, 2147483647, true, DisplayName = "GlobalCacheOptions disabled with explicit Int.MaxValue ttl: -> provided values used and userDefined flag set to true")]
    [DataRow(@",""cache"": { ""enabled"": true }", true, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "GlobalCacheOptions enabled and ttl left out: provided enabled flag used with default ttl and userdefined flag set to false")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 5 }", true, DEFAULT_CACHE_TTL_SECONDS, true, DisplayName = "GlobalCacheOptions provided: provided values used and userdefined flag set to true")]
    [DataRow(@",""cache"": { ""enabled"": null }", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "GlobalCacheOptions.Enabled set to null results in (default) disabled entity cache.")]
    [DataRow(@",""cache"": { ""enabled"": false, ""ttl-seconds"": null }", false, DEFAULT_CACHE_TTL_SECONDS, false, DisplayName = "GlobalCacheOptions.TtlSeconds set to null results in (default) ttl value.")]
    [DataTestMethod]
    public void GlobalCacheOptionsDeserialization_ValidValues(
        string globalCacheConfig,
        bool expectCacheEnabled,
        int expectedTTL,
        bool expectedUserDefinedTtl)
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
            replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsNotNull(config, message: "Config must not be null, runtime config JSON deserialization failed.");
        Assert.AreEqual(expected: expectCacheEnabled, actual: config.IsCachingEnabled, message: "RuntimeConfig.CacheEnabled expected to be: " + expectCacheEnabled);

        EntityCacheOptions? resolvedGlobalCacheOptions = config?.Runtime?.Cache;
        if (expectCacheEnabled)
        {
            Assert.IsNotNull(config?.IsCachingEnabled, message: "Expected global cache property to be non-null.");
            Assert.IsNotNull(resolvedGlobalCacheOptions, message: "GlobalCacheConfig must not be null, unexpected entity JSON deserialization result.");
            Assert.AreEqual(expectCacheEnabled, resolvedGlobalCacheOptions.Enabled, message: "GlobalCacheConfig.Enabled expected to be: " + expectCacheEnabled);
            Assert.AreEqual(expectedTTL, resolvedGlobalCacheOptions.TtlSeconds);
            Assert.AreEqual(expectedUserDefinedTtl, resolvedGlobalCacheOptions.UserProvidedTtlOptions, message: "UserProvidedTtlOptions expected to be: " + expectedUserDefinedTtl);
        }
    }

    /// <summary>
    /// Validates that unexpected values provided for global runtime.cache property in the JSON config result in
    /// a failure to deserialize the runtime config.
    /// </summary>
    /// <param name="globalCacheConfig">Escaped JSON string defining entity cache configuration.</param>
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 2147483648 }", DisplayName = "EntityCacheOptions.TtlSeconds set to Int.MaxValue+1 is invalid for parser.")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": -2147483649 }", DisplayName = "EntityCacheOptions.TtlSeconds set to Int.MinValue-1 is invalid for parser.")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 0 }", DisplayName = "EntityCacheOptions.TtlSeconds set to zero is invalid configuration.")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": -1 }", DisplayName = "EntityCacheOptions.TtlSeconds set to negative number is invalid configuration.")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 1.1 }", DisplayName = "EntityCacheOptions.TtlSeconds set to decimal is invalid configuration.")]
    [DataRow(@",""cache"": { ""enabled"": 1 }", DisplayName = "EntityCacheOptions.Enabled property set to 1 should fail because not a boolean.")]
    [DataRow(@",""cache"": { ""enabled"": 0 }", DisplayName = "EntityCacheOptions.Enabled property set to 0 should fail because not a boolean.")]
    [DataRow(@",""cache"": 1", DisplayName = "EntityCacheOptions property set to 1 should fail because it's not a JSON object.")]
    [DataRow(@",""cache"": 0", DisplayName = "EntityCacheOptions property set to 0 should fail because it's not a JSON object.")]
    [DataRow(@",""cache"": true", DisplayName = "EntityCacheOptions property set to true should fail because it's not a JSON object.")]
    [DataRow(@",""cache"": false", DisplayName = "EntityCacheOptions property set to false should fail because it's not a JSON object.")]
    [DataTestMethod]
    public void GlobalCacheOptionsDeserialization_InvalidValues(string globalCacheConfig)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig: globalCacheConfig, entityCacheConfig: string.Empty);

        // Act
        bool parsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            json: fullConfig,
            out _,
            logger: null,
            connectionString: null,
            replaceEnvVar: false,
            replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsFalse(parsingSuccessful, message: "Expected JSON parsing to fail.");
    }

    /// <summary>
    /// Validates that an entity's cache config ttl is used when both the global cache config ttl and the entity cache config ttl are explicitly set.
    /// Validates that the explicitly configured global cache config ttl is NOT used in an entity's cache config
    /// when an entity's cache config ttl is not explicitly set -> This behavior is only expected at runtime and not in config (de)serialization.
    /// </summary>
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 10 }", @",""cache"": { ""enabled"": true, ""ttl-seconds"": 10 }", 10, 10, DisplayName = "EntityCacheTTL honored")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 15 }", @",""cache"": { ""enabled"": true, ""ttl-seconds"": 10 }", 15, 10, DisplayName = "EntityCacheTTL honored over differing global default ttl.")]
    [DataRow(@",""cache"": { ""enabled"": true, ""ttl-seconds"": 15 }", @",""cache"": { ""enabled"": true }", 15, 5, DisplayName = "GlobalTTL not yet honored over unset entity ttl. This behavior will be in runtime behavior and not in config (de)serialization.")]
    [DataRow(@",""cache"": { ""enabled"": true }", @",""cache"": { ""enabled"": true, ""ttl-seconds"": 10 }", DEFAULT_CACHE_TTL_SECONDS, 10, DisplayName = "EntityCacheTTL honored over unset global ttl.")]
    [DataRow(@",""cache"": { ""enabled"": true }", @",""cache"": { ""enabled"": true }", DEFAULT_CACHE_TTL_SECONDS, DEFAULT_CACHE_TTL_SECONDS, DisplayName = "Default ttl honored for both global and entity cache config ttl.")]
    [DataTestMethod]
    public void GlobalCacheOptionsOverridesEntityCacheOptions(string globalCacheConfig, string entityCacheConfig, int expectedGlobalCacheTtl, int expectedEntityCacheTtl)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig, entityCacheConfig);

        // Act
        RuntimeConfigLoader.TryParseConfig(
                       json: fullConfig,
                       out RuntimeConfig? config,
                       logger: null,
                       connectionString: null,
                       replaceEnvVar: false,
                       replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        // Assert
        Assert.IsNotNull(config, message: "Config must not be null, runtime config JSON deserialization failed.");

        EntityCacheOptions? resolvedGlobalCacheOptions = config?.Runtime?.Cache;
        Assert.IsNotNull(config?.IsCachingEnabled, message: "Expected global cache property to be non-null.");
        Assert.IsNotNull(resolvedGlobalCacheOptions, message: "GlobalCacheConfig must not be null, unexpected JSON deserialization result.");
        Assert.AreEqual(expected: expectedGlobalCacheTtl, actual: resolvedGlobalCacheOptions.TtlSeconds);

        Entity entity = config.Entities.First().Value;
        EntityCacheOptions? resolvedEntityCacheOptions = entity.Cache;
        Assert.IsNotNull(resolvedEntityCacheOptions, message: "EntityCacheConfig must not be null, unexpected entity JSON deserialization result.");
        Assert.AreEqual(expected: expectedEntityCacheTtl, actual: resolvedEntityCacheOptions.TtlSeconds);
    }

    /// <summary>
    /// Validates that when a user explicitly sets a value for ttl-seconds in the cache config,
    /// that value is serialized into JSON when the runtime config is serialized. (Occurs when DAB CLI handles configs.)
    /// </summary>
    /// <param name="expectIsUserDefinedTtl">Whether to expect ttl-seconds property/value to be serialized.</param>
    /// <param name="userDefinedTtl">Expected number of seconds defined for ttl-seconds.</param>
    [DataRow(true, 10, DisplayName = "TTL explicitly set to non-default value and should be serialized.")]
    [DataRow(true, 5, DisplayName = "TTL explicitly set to default value and should be serialized.")]
    [DataTestMethod]
    public void UserDefinedTtlWrittenToSerializedJsonConfigFile(bool expectIsUserDefinedTtl, int userDefinedTtl)
    {
        // Arrange
        string cacheConfig = @",""cache"": { ""enabled"": true, ""ttl-seconds"": " + userDefinedTtl + " }";
        string fullConfig = GetRawConfigJson(globalCacheConfig: cacheConfig, entityCacheConfig: cacheConfig);
        RuntimeConfigLoader.TryParseConfig(
                       json: fullConfig,
                       out RuntimeConfig? config,
                       logger: null,
                       connectionString: null,
                       replaceEnvVar: false,
                       replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);
        Assert.IsNotNull(config, message: "Test setup failure. Config must not be null, runtime config JSON deserialization failed.");

        // Act
        string serializedConfig = config.ToJson();

        // Assert
        using (JsonDocument parsedRuntimeConfigDom = JsonDocument.Parse(serializedConfig))
        {
            JsonElement root = parsedRuntimeConfigDom.RootElement;

            // Validate global cache config in runtimeConfig.runtime section.
            JsonElement runtimeElement = root.GetProperty("runtime");
            bool cachePropertyExists = runtimeElement.TryGetProperty("cache", out JsonElement globalCacheElement);
            Assert.AreEqual(expected: true, actual: cachePropertyExists);

            bool cacheTtlPropertyExists = globalCacheElement.TryGetProperty("ttl-seconds", out JsonElement ttl);
            Assert.IsTrue(cacheTtlPropertyExists);
            Assert.AreEqual(expected: userDefinedTtl, actual: ttl.GetInt32());

            // Validate entity cache config in runtimeConfig.entities section.
            JsonElement entitiesElement = root.GetProperty("entities");
            JsonElement entityElement = entitiesElement.EnumerateObject().First().Value;
            cachePropertyExists = entityElement.TryGetProperty("cache", out JsonElement entityCacheElement);
            Assert.AreEqual(expected: true, actual: cachePropertyExists);
            Assert.AreEqual(expected: userDefinedTtl, actual: ttl.GetInt32());
        }
    }

    /// <summary>
    /// Validates that when a user does not explicitly set a value for the cache property,
    /// the cache property and its defaults (enabled: false, ttl-seconds: 5) will NOT be serialized
    /// to the JSON config file. (Occurs when DAB CLI handles configs.)
    /// </summary>
    /// <param name="cacheConfig">JSON cache configuration. Can be left empty to indicate no caching config.</param>
    [DataRow(@",""cache"": null", DisplayName = "Null cache object should not be serialized to config file.")]
    [DataRow(@"", DisplayName = "Excluded cache property object should not result in serialized cache object in config file.")]
    [TestMethod]
    public void CachePropertyNotWrittenToSerializedJsonConfigFile(string cacheConfig)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig: cacheConfig, entityCacheConfig: cacheConfig);
        RuntimeConfigLoader.TryParseConfig(
                       json: fullConfig,
                       out RuntimeConfig? config,
                       logger: null,
                       connectionString: null,
                       replaceEnvVar: false,
                       replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);
        Assert.IsNotNull(config, message: "Test setup failure. Config must not be null, runtime config JSON deserialization failed.");

        // Act
        string serializedConfig = config.ToJson();

        // Assert
        using (JsonDocument parsedRuntimeConfigDom = JsonDocument.Parse(serializedConfig))
        {
            JsonElement root = parsedRuntimeConfigDom.RootElement;

            // Validate global cache config in runtimeConfig.runtime section.
            JsonElement runtimeElement = root.GetProperty("runtime");
            bool cachePropertyExists = runtimeElement.TryGetProperty("cache", out JsonElement globalCacheElement);
            Assert.AreEqual(expected: false, actual: cachePropertyExists, message: "Global cache property should not be serialized to config file.");

            // Validate entity cache config in runtimeConfig.entities section.
            JsonElement entitiesElement = root.GetProperty("entities");
            JsonElement entityElement = entitiesElement.EnumerateObject().First().Value;
            cachePropertyExists = entityElement.TryGetProperty("cache", out JsonElement entityCacheElement);
            Assert.AreEqual(expected: false, actual: cachePropertyExists, message: "Entity cache property should not be serialized to config file.");
        }
    }

    /// <summary>
    /// Validates that when ttl-seconds is not set by the user explicitly, the default value implicitly used by engine (5) is used and
    /// the property/value should not be serialized to the JSON config file because it was not provided initially.
    /// </summary>
    /// <param name="cacheConfig">JSON cache configuration. Can be left empty to indicate no caching config.</param>
    [DataRow(@",""cache"": { }", DisplayName = "Empty cache object should be serialized to config file with default enabled:false and no ttl-seconds property/value.")]
    [DataRow(@",""cache"": { ""enabled"": true }", DisplayName = "Cache object with no users defined ttl should be serialized to config file without ttl-seconds property/value.")]
    [DataTestMethod]
    public void DefaultTtlNotWrittenToSerializedJsonConfigFile(string cacheConfig)
    {
        // Arrange
        string fullConfig = GetRawConfigJson(globalCacheConfig: cacheConfig, entityCacheConfig: cacheConfig);
        RuntimeConfigLoader.TryParseConfig(
                       json: fullConfig,
                       out RuntimeConfig? config,
                       logger: null,
                       connectionString: null,
                       replaceEnvVar: false,
                       replacementFailureMode: EnvironmentVariableReplacementFailureMode.Throw);
        Assert.IsNotNull(config, message: "Test setup failure. Config must not be null, runtime config JSON deserialization failed.");

        // Act
        string serializedConfig = config.ToJson();

        // Assert
        using (JsonDocument parsedRuntimeConfigDom = JsonDocument.Parse(serializedConfig))
        {
            JsonElement root = parsedRuntimeConfigDom.RootElement;

            // Validate global cache config in runtimeConfig.runtime section.
            bool cachePropertyExists = root.GetProperty("runtime").TryGetProperty("cache", out JsonElement globalCacheElement);
            Assert.AreEqual(expected: true, actual: cachePropertyExists);

            bool globalCacheTtlPropertyExists = globalCacheElement.TryGetProperty("ttl-seconds", out JsonElement _);
            Assert.IsFalse(globalCacheTtlPropertyExists, message: "Global cache TTL property/value pair should not be serialized to config file.");

            // Validate entity cache config in runtimeConfig.entities section.
            JsonElement entityElement = root.GetProperty("entities").EnumerateObject().First().Value;
            cachePropertyExists = entityElement.TryGetProperty("cache", out JsonElement entityCacheElement);
            Assert.AreEqual(expected: true, actual: cachePropertyExists, message: "Global Cache property expected to be serialized to config file.");

            bool entityCacheTtlPropertyExists = entityCacheElement.TryGetProperty("ttl-seconds", out JsonElement _);
            Assert.IsFalse(entityCacheTtlPropertyExists, message: "Global cache TTL property/value pair should not be serialized to config file.");
        }
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

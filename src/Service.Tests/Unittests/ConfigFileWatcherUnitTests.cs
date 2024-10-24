// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public class ConfigFileWatcherUnitTests
    {

        private static string GenerateRuntimeSectionStringFromParams(
            string connectionString,
            string restPath,
            string gqlPath,
            bool restEnabled,
            bool gqlEnabled,
            bool gqlIntrospection,
            HostMode mode
            )
        {
            string runtimeString = @"
{
  ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": """ + connectionString + @""",
    ""options"": {
      ""set-session-context"": true
    }
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": " + restEnabled.ToString().ToLower() + @",
      ""path"": """ + restPath + @"""
    },
    ""graphql"": {
      ""enabled"": " + gqlEnabled.ToString().ToLower() + @",
      ""path"": """ + gqlPath + @""",
      ""allow-introspection"": " + gqlIntrospection.ToString().ToLower() + @"
    },
    ""host"": {
      ""cors"": {
        ""origins"": [
          ""http://localhost:5000""
        ],
        ""allow-credentials"": false
      },
      ""authentication"": {
        ""provider"": ""StaticWebApps""
      },
      ""mode"": """ + mode + @"""
    }
  },
  ""entities"": {}
}";
            return runtimeString;
        }

        private static string GenerateWrongRuntimeSection()
        {
            string runtimeString = @"
{
  ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": ""Server=test;Database=test;User ID=test;"",
    ""options"": {
      ""set-session-context"": true
    }
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"":
}";
            return runtimeString;
        }

        private static string GenerateWrongSchema()
        {
            string schemaString = @"
{
    ""$schema"": ""https://json-schema.org/draft-07/schema"",
    ""$id"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
    ""title"": ""Data API builder"",
    ""description"": ""Schema for 
}";
            return schemaString;
        }

        /// <summary>
        /// Use the file system (not mocked) to create a hot reload
        /// scenario of the REST runtime options and verify that we
        /// correctly hot reload those options.
        /// NOTE: This test is ignored until we have the possibility of turning
        /// on hot reload.
        /// </summary>
        [TestMethod]
        [Ignore]
        public void HotReloadConfigRestRuntimeOptions()
        {
            // Arrange
            // 1. Setup the strings that are turned into our initital and runtime config to reload.
            // 2. Generate initial runtimeconfig, start file watching, and assert we have valid initial values.
            string connectionString = ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL);

            string initialRestPath = "/api";
            string updatedRestPath = "/rest";
            string initialGQLPath = "/api";
            string updatedGQLPath = "/gql";

            bool initialRestEnabled = true;
            bool updatedRestEnabled = false;
            bool initialGQLEnabled = true;
            bool updatedGQLEnabled = false;

            bool initialGQLIntrospection = true;
            bool updatedGQLIntrospection = false;

            HostMode initialMode = HostMode.Development;
            HostMode updatedMode = HostMode.Production;

            string initialConfig = GenerateRuntimeSectionStringFromParams(
                connectionString,
                initialRestPath,
                initialGQLPath,
                initialRestEnabled,
                initialGQLEnabled,
                initialGQLIntrospection,
                initialMode);

            string updatedConfig = GenerateRuntimeSectionStringFromParams(
                connectionString,
                updatedRestPath,
                updatedGQLPath,
                updatedRestEnabled,
                updatedGQLEnabled,
                updatedGQLIntrospection,
                updatedMode);

            string configName = "hotreload-config.json";
            if (File.Exists(configName))
            {
                File.Delete(configName);
            }

            // Not using mocked filesystem so we pick up real file changes for hot reload
            FileSystem fileSystem = new();
            fileSystem.File.WriteAllText(configName, initialConfig);
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, handler: null, configName, string.Empty);
            RuntimeConfigProvider configProvider = new(configLoader);

            // Must GetConfig() to start file watching
            RuntimeConfig runtimeConfig = configProvider.GetConfig();
            string initialDefaultDataSourceName = runtimeConfig.DefaultDataSourceName;

            // assert we have a valid config
            Assert.IsNotNull(runtimeConfig);
            Assert.AreEqual(initialRestEnabled, runtimeConfig.Runtime.Rest.Enabled);
            Assert.AreEqual(initialRestPath, runtimeConfig.Runtime.Rest.Path);
            Assert.AreEqual(initialGQLEnabled, runtimeConfig.Runtime.GraphQL.Enabled);
            Assert.AreEqual(initialGQLPath, runtimeConfig.Runtime.GraphQL.Path);
            Assert.AreEqual(initialGQLIntrospection, runtimeConfig.Runtime.GraphQL.AllowIntrospection);
            Assert.AreEqual(initialMode, runtimeConfig.Runtime.Host.Mode);

            // Simulate change to the config file
            fileSystem.File.WriteAllText(configName, updatedConfig);

            // Give ConfigFileWatcher enough time to hot reload the change
            System.Threading.Thread.Sleep(1000);

            // Act
            // 1. Hot reload the runtime config
            runtimeConfig = configProvider.GetConfig();
            string updatedDefaultDataSourceName = runtimeConfig.DefaultDataSourceName;

            // Assert
            // 1. Assert we have the correct values after a hot reload.
            Assert.AreEqual(updatedRestEnabled, runtimeConfig.Runtime.Rest.Enabled);
            Assert.AreEqual(updatedRestPath, runtimeConfig.Runtime.Rest.Path);
            Assert.AreEqual(updatedGQLEnabled, runtimeConfig.Runtime.GraphQL.Enabled);
            Assert.AreEqual(updatedGQLPath, runtimeConfig.Runtime.GraphQL.Path);
            Assert.AreEqual(updatedGQLIntrospection, runtimeConfig.Runtime.GraphQL.AllowIntrospection);
            Assert.AreEqual(updatedMode, runtimeConfig.Runtime.Host.Mode);

            // DefaultDataSourceName should not change after a hot reload.
            Assert.AreEqual(initialDefaultDataSourceName, updatedDefaultDataSourceName);
            if (File.Exists(configName))
            {
                File.Delete(configName);
            }
        }

        /// <summary>
        /// Creates a hot reload scenario in which the schema is wrong which causes
        /// hot reload to fail, then we check that the program is still able to work
        /// properly by showing us that it is still using the same configuration file
        /// from before the hot reload.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public void HotReloadValidationFail()
        {
            //Arrange
            string schemaName = "dab.draft.schema.json";
            string configName = "hotreload-config.json";

            bool initialRestEnabled = true;
            bool updatedRestEnabled = false;

            bool initialGQLEnabled = true;
            bool updatedGQLEnabled = false;

            DataSource dataSource = new(DatabaseType.MSSQL,
                ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig initialConfig = new(
                Schema: schemaName,
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(initialRestEnabled),
                    GraphQL: new(initialGQLEnabled),
                    Host: new(null, null, HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );

            RuntimeConfig updatedConfig = new(
                Schema: schemaName,
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(updatedRestEnabled),
                    GraphQL: new(updatedGQLEnabled),
                    Host: new(null, null, HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );

            if (File.Exists(configName))
            {
                File.Delete(configName);
            }

            string schemaConfig = GenerateWrongSchema();

            // Not using mocked filesystem so we pick up real file changes for hot reload
            FileSystem fileSystem = new();
            fileSystem.File.WriteAllText(configName, initialConfig.ToJson());
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, handler: null, configName, string.Empty);
            RuntimeConfigProvider configProvider = new(configLoader);
            RuntimeConfig lkgRuntimeConfig = configProvider.GetConfig();

            Assert.IsNotNull(lkgRuntimeConfig);

            //Act
            // Simulate a wrong change to the config file
            fileSystem.File.WriteAllText(schemaName, schemaConfig);
            fileSystem.File.WriteAllText(configName, updatedConfig.ToJson());

            // Give ConfigFileWatcher enough time to hot reload the change
            System.Threading.Thread.Sleep(1000);

            try
            {
                configProvider.GetConfig();
            }
            catch (DataApiBuilderException dbException)
            {
                Assert.AreEqual(expected: "Failed validation of configuration file.", actual: dbException.Message);
            }

            RuntimeConfig newRuntimeConfig = configProvider.GetConfig();
            Assert.AreEqual(expected: lkgRuntimeConfig, actual: newRuntimeConfig);

            if (File.Exists(configName))
            {
                File.Delete(configName);
            }

            if (File.Exists(schemaName))
            {
                File.Delete(schemaName);
            }
        }

        /// <summary>
        /// Creates a hot reload scenario in which the updated configuration file is wrong so
        /// hot reload to fail, then we check that the program is still able to work
        /// properly by showing us that it is still using the same configuration file
        /// from before the hot reload.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public void HotReloadParsingFail()
        {
            //Arrange
            string connectionString = ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL);
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = false,
                PropertyNamingPolicy = new HyphenatedNamingPolicy(),
                ReadCommentHandling = JsonCommentHandling.Skip,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            JsonSerializer.Serialize(connectionString, options);

            string restPath = "/api";
            string gQLPath = "/api";

            bool restEnabled = false;
            bool initialGQLEnabled = false;

            bool initialGQLIntrospection = false;

            HostMode mode = HostMode.Development;

            string initialConfig = GenerateRuntimeSectionStringFromParams(
                    connectionString,
                    restPath,
                    gQLPath,
                    restEnabled,
                    initialGQLEnabled,
                    initialGQLIntrospection,
                    mode);

            string updatedConfig = GenerateWrongRuntimeSection();

            string configName = "hotreload-config.json";
            if (File.Exists(configName))
            {
                File.Delete(configName);
            }

            // Not using mocked filesystem so we pick up real file changes for hot reload
            FileSystem fileSystem = new();
            fileSystem.File.WriteAllText(configName, initialConfig);
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, handler: null, configName, string.Empty);
            RuntimeConfigProvider configProvider = new(configLoader);
            RuntimeConfig lkgRuntimeConfig = configProvider.GetConfig();

            Assert.IsNotNull(lkgRuntimeConfig);

            //Act
            // Simulate a wrong change to the config file
            fileSystem.File.WriteAllText(configName, updatedConfig);

            // Give ConfigFileWatcher enough time to hot reload the change
            System.Threading.Thread.Sleep(1000);

            RuntimeConfig newRuntimeConfig = configProvider.GetConfig();
            Assert.AreEqual(expected: lkgRuntimeConfig, actual: newRuntimeConfig);

            if (File.Exists(configName))
            {
                File.Delete(configName);
            }
        }
    }
}

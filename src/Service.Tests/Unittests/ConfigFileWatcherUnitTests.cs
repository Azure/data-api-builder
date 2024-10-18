// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public class ConfigFileWatcherUnitTests
    {

        private static string GenerateRuntimeSectionStringFromParams(
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
    ""connection-string"": ""Server=test;Database=test;User ID=test;"",
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
                initialRestPath,
                initialGQLPath,
                initialRestEnabled,
                initialGQLEnabled,
                initialGQLIntrospection,
                initialMode);

            string updatedConfig = GenerateRuntimeSectionStringFromParams(
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
        /// This test validates that overwriting a file with the different contents
        /// results in ConfigFileWatcher's NewFileContentsDetected event being raised.
        /// Internally, ConfigFileWatcher.NewFileContentsDetected is raised when the hash
        /// of the file contents differ from what was previously detected. For this test,
        /// the file contents differ MyValue -> MyChangedValue.
        /// The config file name is specific to this test in order to avoid concurrently
        /// running tests from stepping over eachother due to acquiring a handle(s) to the file.
        /// </summary>
        [TestMethod]
        public void ConfigFileWatcher_NotifiedOfOneNetNewChanges()
        {
            // Arrange
            int fileChangeNotificationsReceived = 0;
            string configName = "ConfigFileWatcher_NotifiedOfOneNetNewChanges.json";
            File.WriteAllText(configName, "MyValue");
            ConfigFileWatcher fileWatcher = new(directoryName: Directory.GetCurrentDirectory(), configFileName: configName);
            fileWatcher.NewFileContentsDetected += (sender, e) =>
            {
                // For testing, modification of fileChangeNotificationsRecieved
                // should be atomic. 
                Interlocked.Increment(ref fileChangeNotificationsReceived);
            };

            // Act - Write to the file twice to implicitly trigger
            // multiple file change events for the ConfigFileWatcher's filesystem watcher.
            ModifyConfigFile(configFileName: configName, configContent: "MyChangedValue");

            // Assert -> allow time for the file change events to be processed.
            // Wait time is arbitrary.
            Thread.Sleep(millisecondsTimeout: 500);
            Assert.AreEqual(expected: 1, actual: fileChangeNotificationsReceived);
        }

        /// <summary>
        /// This test validates that overwriting a file with the same contents does not
        /// result in ConfigFileWatcher's NewFileContentsDetected event being raised.
        /// Internally, ConfigFileWatcher.NewFileContentsDetected is raised when the hash
        /// of the file contents differ from what was previously detected.
        /// In this test, the file contents are the same: MyValue -> MyValue.
        /// The config file name is specific to this test in order to avoid concurrently
        /// running tests from stepping over eachother due to acquiring a handle(s) to the file.
        /// </summary>
        [TestMethod]
        public void ConfigFileWatcher_NotifiedOfZeroNetNewChange()
        {
            // Arrange
            int fileChangeNotificationsReceived = 0;
            string configName = "ConfigFileWatcher_NotifiedOfZeroNetNewChange.json";

            // Write initial value to file.
            File.WriteAllText(configName, "MyValue");

            ConfigFileWatcher fileWatcher = new(directoryName: Directory.GetCurrentDirectory(), configFileName: configName);
            fileWatcher.NewFileContentsDetected += (sender, e) =>
            {
                // For testing, modification of fileChangeNotificationsRecieved
                // should be atomic. 
                Interlocked.Increment(ref fileChangeNotificationsReceived);
            };

            // Act - Write to the file multiple times with the same value to implicitly trigger
            // multiple file change events for the ConfigFileWatcher's filesystem watcher.
            ModifyConfigFile(configFileName: configName, configContent: "MyValue");

            // Assert -> allow time for the file change events to be processed.
            // Wait time is arbitrary.
            Thread.Sleep(millisecondsTimeout: 500);
            Assert.AreEqual(expected: 0, actual: fileChangeNotificationsReceived);
        }

        /// <summary>
        /// Wrapper around File.WriteAllText() such that IOExceptions result in retries
        /// to allow for other handles on file to be dropped.
        /// RunCount and sleep time are arbitrary.
        /// </summary>
        /// <param name="configFileName">Name of write-target config file.</param>
        /// <param name="configContent">Content to write to file.</param>
        private static void ModifyConfigFile(string configFileName, string configContent)
        {
            int runCount = 1;
            while (runCount < 4)
            {
                try
                {
                    File.WriteAllText(configFileName, configContent);
                    return;
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"IO Exception, retrying due to {ex.Message}");
                    if (runCount == 3)
                    {
                        throw;
                    }

                    // Constant backoff because we don't want this to hold up the CI/CD pipelines.
                    // Wait time is arbitrary.
                    Thread.Sleep(millisecondsTimeout: 500);
                    runCount++;
                }
            }
        }
    }
}

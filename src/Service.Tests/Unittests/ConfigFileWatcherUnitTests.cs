// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public class ConfigFileWatcherUnitTests
    {
        /// <summary>
        /// Use the file system to add a file that matches the format of our config file.
        /// Use that to create the needed config loader and provider, and then get the
        /// actual config object from the provider, instantiating our config file watcher
        /// in the process. Modify that file we created originally and verify we reload
        /// the config correctly for the desired RuntimeOptions.
        /// </summary>
        [TestMethod]
        public void HotReloadConfigRestRuntimeOptions()
        {
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

            string initialConfig = @"
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
      ""enabled"": " + initialRestEnabled.ToString().ToLower() + @",
      ""path"": """ + initialRestPath + @"""
    },
    ""graphql"": {
      ""enabled"": " + initialGQLEnabled.ToString().ToLower() + @",
      ""path"": """ + initialGQLPath + @""",
      ""allow-introspection"": " + initialGQLIntrospection.ToString().ToLower() + @"
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
      ""mode"": """ + initialMode + @"""
    }
  },
  ""entities"": {}
}";
            string updatedConfig = @"
{
  ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": ""Server=test;Database=test;User ID=test"",
    ""options"": {
      ""set-session-context"": true
    }
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": " + updatedRestEnabled.ToString().ToLower() + @",
      ""path"": """ + updatedRestPath + @"""
    },
    ""graphql"": {
      ""enabled"": " + updatedGQLEnabled.ToString().ToLower() + @",
      ""path"": """ + updatedGQLPath + @""",
      ""allow-introspection"": " + updatedGQLIntrospection.ToString().ToLower() + @"
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
      ""mode"": """ + updatedMode + @"""
    }
  },
  ""entities"": {}
}";
            string configName = "hotreload-config.json";
            if (File.Exists(configName))
            {
                File.Delete(configName);
            }
            
            FileSystem fileSystem = new();
            fileSystem.File.WriteAllText(configName, initialConfig);
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, configName, string.Empty);
            RuntimeConfigProvider configProvider = new(configLoader);
            // Must GetConfig() to start file watching
            RuntimeConfig runtimeConfig = configProvider.GetConfig();
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
            // Give file watcher enough time to see the change
            System.Threading.Thread.Sleep(1000);
            // Hot Reloaded config
            runtimeConfig = configProvider.GetConfig();
            Assert.AreEqual(updatedRestEnabled, runtimeConfig.Runtime.Rest.Enabled);
            Assert.AreEqual(updatedRestPath, runtimeConfig.Runtime.Rest.Path);
            Assert.AreEqual(updatedGQLEnabled, runtimeConfig.Runtime.GraphQL.Enabled);
            Assert.AreEqual(updatedGQLPath, runtimeConfig.Runtime.GraphQL.Path);
            Assert.AreEqual(updatedGQLIntrospection, runtimeConfig.Runtime.GraphQL.AllowIntrospection);
            Assert.AreEqual(updatedMode, runtimeConfig.Runtime.Host.Mode);
            if (File.Exists(configName))
            {
                File.Delete(configName);
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

[TestClass]
public class RuntimeConfigLoaderTests
{
    [DataTestMethod]
    [DataRow("dab-config.CosmosDb_NoSql.json")]
    [DataRow("dab-config.MsSql.json")]
    [DataRow("dab-config.MySql.json")]
    [DataRow("dab-config.PostgreSql.json")]
    public async Task CanLoadStandardConfig(string configPath)
    {
        string fileContents = await File.ReadAllTextAsync(configPath);

        IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>() { { "dab-config.json", new MockFileData(fileContents) } });

        FileSystemRuntimeConfigLoader loader = new(fs);

        Assert.IsTrue(loader.TryLoadConfig("dab-config.json", out RuntimeConfig _), "Failed to load config");
    }

    /// <summary>
    /// Test validates that when child files are present all datasources are loaded correctly.
    /// </summary>
    [DataTestMethod]
    [DataRow("Multidab-config.CosmosDb_NoSql.json", new string[] { "Multidab-config.MsSql.json", "Multidab-config.MySql.json", "Multidab-config.PostgreSql.json" })]
    public async Task CanLoadValidMultiSourceConfig(string configPath, IEnumerable<string> dataSourceFiles)
    {
        string fileContents = await File.ReadAllTextAsync(configPath);

        // Parse the base JSON string
        JObject baseJsonObject = JObject.Parse(fileContents);

        // Create a new JArray to hold the values to be appended
        JArray valuesToAppend = new(dataSourceFiles);

        // Add or append the values to the base JSON
        baseJsonObject.Add("data-source-files", valuesToAppend);

        // Convert the modified JSON object back to a JSON string
        string resultJson = baseJsonObject.ToString();

        IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>() { { "dab-config.json", new MockFileData(resultJson) } });

        FileSystemRuntimeConfigLoader loader = new(fs);

        Assert.IsTrue(loader.TryLoadConfig("dab-config.json", out RuntimeConfig runtimeConfig), "Should successfully load config");
        Assert.IsTrue(runtimeConfig.ListAllDataSources().Count() == 4, "Should have 4 data sources");
        Assert.IsTrue(runtimeConfig.CosmosDataSourceUsed, "Should have CosmosDb data source");
        Assert.IsTrue(runtimeConfig.SqlDataSourceUsed, "Should have Sql data source");
        Assert.AreEqual(DatabaseType.CosmosDB_NoSQL, runtimeConfig.DataSource.DatabaseType, "Default datasource should be of root file database type.");
    }

    /// <summary>
    /// Test validates that load fails when datasource files have duplicate entities.
    /// Example: Publisher entity present in the 3 sql.json files.
    /// </summary>
    [DataTestMethod]
    [DataRow("dab-config.CosmosDb_NoSql.json", new string[] { "dab-config.MsSql.json", "dab-config.MySql.json", "dab-config.PostgreSql.json" })]
    public async Task FailLoadMultiDataSourceConfigDuplicateEntities(string configPath, IEnumerable<string> dataSourceFiles)
    {
        string fileContents = await File.ReadAllTextAsync(configPath);

        // Parse the base JSON string
        JObject baseJsonObject = JObject.Parse(fileContents);

        // Create a new JArray to hold the values to be appended
        JArray valuesToAppend = new(dataSourceFiles);

        // Add or append the values to the base JSON
        baseJsonObject.Add("data-source-files", valuesToAppend);

        // Convert the modified JSON object back to a JSON string
        string resultJson = baseJsonObject.ToString();

        IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>() { { "dab-config.json", new MockFileData(resultJson) } });

        FileSystemRuntimeConfigLoader loader = new(fs);

        StringWriter sw = new();
        Console.SetError(sw);

        loader.TryLoadConfig("dab-config.json", out RuntimeConfig _);
        string error = sw.ToString();

        Assert.IsTrue(error.StartsWith("Deserialization of the configuration file failed during a post-processing step."));
        Assert.IsTrue(error.Contains("An item with the same key has already been added."));
    }

    /// <summary>
    /// Test validates that when child files are present all autoentities are loaded correctly.
    /// </summary>
    [DataTestMethod]
    [DataRow("Multidab-config.CosmosDb_NoSql.json", new string[] { "Multidab-config.MsSql.json", "Multidab-config.MySql.json", "Multidab-config.PostgreSql.json" }, 36)]
    public async Task CanLoadValidMultiSourceConfigWithAutoentities(string configPath, IEnumerable<string> dataSourceFiles, int expectedEntities)
    {
        string fileContents = await File.ReadAllTextAsync(configPath);

        // Parse the base JSON string
        JObject baseJsonObject = JObject.Parse(fileContents);

        // Create a new JArray to hold the values to be appended
        JArray valuesToAppend = new(dataSourceFiles);

        // Add or append the values to the base JSON
        baseJsonObject.Add("data-source-files", valuesToAppend);

        // Convert the modified JSON object back to a JSON string
        string resultJson = baseJsonObject.ToString();

        IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>() { { "dab-config.json", new MockFileData(resultJson) } });

        FileSystemRuntimeConfigLoader loader = new(fs);

        Assert.IsTrue(loader.TryLoadConfig("dab-config.json", out RuntimeConfig runtimeConfig), "Should successfully load config");
        Assert.IsTrue(runtimeConfig.SqlDataSourceUsed, "Should have Sql data source");
        Assert.AreEqual(expectedEntities, runtimeConfig.Entities.Entities.Count, "Number of entities is not what is expected.");
    }

    /// <summary>
    /// Validates that when a child config contains @env('...') references to environment variables
    /// that do not exist, the config still loads successfully because the child config uses
    /// EnvironmentVariableReplacementFailureMode.Ignore (matching the parent config behavior).
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3271
    /// </summary>
    [TestMethod]
    public async Task ChildConfigWithMissingEnvVarsLoadsSuccessfully()
    {
        string parentConfig = await File.ReadAllTextAsync("Multidab-config.MsSql.json");

        // Child config references env vars that do not exist in the environment.
        string childConfig = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
            },
            ""runtime"": {
                ""rest"": { ""enabled"": true },
                ""graphql"": { ""enabled"": true },
                ""host"": {
                    ""cors"": { ""origins"": [] },
                    ""authentication"": { ""provider"": ""StaticWebApps"" }
                },
                ""telemetry"": {
                    ""open-telemetry"": {
                        ""enabled"": true,
                        ""endpoint"": ""@env('NONEXISTENT_OTEL_ENDPOINT')"",
                        ""headers"": ""@env('NONEXISTENT_OTEL_HEADERS')"",
                        ""service-name"": ""@env('NONEXISTENT_OTEL_SERVICE_NAME')""
                    }
                }
            },
            ""entities"": {
                ""ChildEntity"": {
                    ""source"": ""dbo.ChildTable"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""read""] }]
                }
            }
        }";

        // Save original env var values and clear them to ensure they don't exist.
        string? origEndpoint = Environment.GetEnvironmentVariable("NONEXISTENT_OTEL_ENDPOINT");
        string? origHeaders = Environment.GetEnvironmentVariable("NONEXISTENT_OTEL_HEADERS");
        string? origServiceName = Environment.GetEnvironmentVariable("NONEXISTENT_OTEL_SERVICE_NAME");
        Environment.SetEnvironmentVariable("NONEXISTENT_OTEL_ENDPOINT", null);
        Environment.SetEnvironmentVariable("NONEXISTENT_OTEL_HEADERS", null);
        Environment.SetEnvironmentVariable("NONEXISTENT_OTEL_SERVICE_NAME", null);

        // Write the child config to a unique temp file because the RuntimeConfig
        // constructor creates a real FileSystem to load child data-source-files.
        string childFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            await File.WriteAllTextAsync(childFilePath, childConfig);

            JObject parentJson = JObject.Parse(parentConfig);
            parentJson.Add("data-source-files", new JArray(childFilePath));
            string parentJsonStr = parentJson.ToString();

            MockFileSystem fs = new(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(parentJsonStr) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);

            DeserializationVariableReplacementSettings replacementSettings = new(
                azureKeyVaultOptions: null,
                doReplaceEnvVar: true,
                doReplaceAkvVar: false,
                envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore);

            Assert.IsTrue(
                loader.TryLoadConfig("dab-config.json", out RuntimeConfig runtimeConfig, replacementSettings: replacementSettings),
                "Config should load successfully even when child config has missing env vars.");

            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("ChildEntity"), "Child config entity should be merged into the parent config.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NONEXISTENT_OTEL_ENDPOINT", origEndpoint);
            Environment.SetEnvironmentVariable("NONEXISTENT_OTEL_HEADERS", origHeaders);
            Environment.SetEnvironmentVariable("NONEXISTENT_OTEL_SERVICE_NAME", origServiceName);

            if (File.Exists(childFilePath))
            {
                File.Delete(childFilePath);
            }
        }
    }

    /// <summary>
    /// Validates that when a child config file cannot be loaded (e.g. file not found, invalid JSON),
    /// the parent config loading fails instead of silently skipping the child.
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3271
    /// </summary>
    [TestMethod]
    public async Task ChildConfigLoadFailureHaltsParentConfigLoading()
    {
        string parentConfig = await File.ReadAllTextAsync("Multidab-config.MsSql.json");

        JObject parentJson = JObject.Parse(parentConfig);
        parentJson.Add("data-source-files", new JArray("nonexistent-child.json"));
        string parentJsonStr = parentJson.ToString();

        MockFileSystem fs = new(new Dictionary<string, MockFileData>()
        {
            { "dab-config.json", new MockFileData(parentJsonStr) }
            // nonexistent-child.json intentionally NOT added to the mock file system
        });

        FileSystemRuntimeConfigLoader loader = new(fs);

        TextWriter originalError = Console.Error;
        StringWriter sw = new();
        Console.SetError(sw);

        try
        {
            bool loaded = loader.TryLoadConfig("dab-config.json", out RuntimeConfig _);
            string error = sw.ToString();

            Assert.IsFalse(loaded, "Config loading should fail when a child config file cannot be loaded.");
            Assert.IsTrue(error.Contains("Failed to load datasource file"), "Error message should indicate the child config file that failed to load.");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}

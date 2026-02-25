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
    [DataRow("Multidab-config.CosmosDb_NoSql.json", new string[] { "Multidab-config.MsSql.json", "Multidab-config.MySql.json", "Multidab-config.PostgreSql.json" }, 10)]
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
}

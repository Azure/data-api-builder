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

        Assert.IsTrue(loader.IsParseErrorEmitted,
            "IsParseErrorEmitted should be true when config parsing fails.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(sw.ToString()),
            "An error message should have been emitted to Console.Error.");
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
    /// Validates that when a parent config has azure-key-vault options configured,
    /// child configs can resolve @akv('...') references using the parent's AKV configuration.
    /// Uses a local .akv file to simulate Azure Key Vault without requiring a real vault.
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3322
    /// </summary>
    [TestMethod]
    public async Task ChildConfigResolvesAkvReferencesFromParentAkvOptions()
    {
        string akvFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".akv");
        string childFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        try
        {
            // Create a local .akv secrets file with test secrets.
            await File.WriteAllTextAsync(akvFilePath, "my-connection-secret=Server=tcp:127.0.0.1,1433;Trusted_Connection=True;\n");

            // Parent config with azure-key-vault pointing to the local .akv file.
            string parentConfig = $@"{{
                ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
                ""data-source"": {{
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
                }},
                ""azure-key-vault"": {{
                    ""endpoint"": ""{akvFilePath.Replace("\\", "\\\\")}""
                }},
                ""data-source-files"": [""{childFilePath.Replace("\\", "\\\\")}""],
                ""runtime"": {{
                    ""rest"": {{ ""enabled"": true }},
                    ""graphql"": {{ ""enabled"": true }},
                    ""host"": {{
                        ""cors"": {{ ""origins"": [] }},
                        ""authentication"": {{ ""provider"": ""StaticWebApps"" }}
                    }}
                }},
                ""entities"": {{}}
            }}";

            // Child config with @akv('...') reference in its connection string.
            string childConfig = @"{
                ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
                ""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""@akv('my-connection-secret')""
                },
                ""runtime"": {
                    ""rest"": { ""enabled"": true },
                    ""graphql"": { ""enabled"": true },
                    ""host"": {
                        ""cors"": { ""origins"": [] },
                        ""authentication"": { ""provider"": ""StaticWebApps"" }
                    }
                },
                ""entities"": {
                    ""AkvChildEntity"": {
                        ""source"": ""dbo.AkvTable"",
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""read""] }]
                    }
                }
            }";

            await File.WriteAllTextAsync(childFilePath, childConfig);

            MockFileSystem fs = new(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(parentConfig) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);

            DeserializationVariableReplacementSettings replacementSettings = new(
                azureKeyVaultOptions: new AzureKeyVaultOptions() { Endpoint = akvFilePath, UserProvidedEndpoint = true },
                doReplaceEnvVar: true,
                doReplaceAkvVar: true,
                envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore);

            Assert.IsTrue(
                loader.TryLoadConfig("dab-config.json", out RuntimeConfig runtimeConfig, replacementSettings: replacementSettings),
                "Config should load successfully when child config has @akv() references resolvable via parent AKV options.");

            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("AkvChildEntity"), "Child config entity should be merged into the parent config.");

            // Verify the child's connection string was resolved from the .akv file.
            string childDataSourceName = runtimeConfig.GetDataSourceNameFromEntityName("AkvChildEntity");
            DataSource childDataSource = runtimeConfig.GetDataSourceFromDataSourceName(childDataSourceName);
            Assert.IsTrue(
                childDataSource.ConnectionString.Contains("127.0.0.1"),
                "Child config connection string should have the AKV secret resolved.");
        }
        finally
        {
            if (File.Exists(akvFilePath))
            {
                File.Delete(akvFilePath);
            }

            if (File.Exists(childFilePath))
            {
                File.Delete(childFilePath);
            }
        }
    }

    /// <summary>
    /// Validates that when both the parent and child configs define azure-key-vault options,
    /// the child's AKV settings take precedence over the parent's.
    /// The child config references a secret that only exists in the child's .akv file,
    /// proving the child's AKV endpoint was used instead of the parent's.
    /// </summary>
    [TestMethod]
    public async Task ChildAkvOptionsOverrideParentAkvOptions()
    {
        string parentAkvFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".akv");
        string childAkvFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".akv");
        string childFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        try
        {
            // Parent's .akv file does NOT contain the secret the child references.
            await File.WriteAllTextAsync(parentAkvFilePath, "parent-only-secret=ParentValue\n");

            // Child's .akv file contains the secret the child references.
            await File.WriteAllTextAsync(childAkvFilePath, "child-connection-secret=Server=tcp:10.0.0.1,1433;Trusted_Connection=True;\n");

            // Parent config with azure-key-vault pointing to the parent's .akv file.
            string parentConfig = $@"{{
                ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
                ""data-source"": {{
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
                }},
                ""azure-key-vault"": {{
                    ""endpoint"": ""{parentAkvFilePath.Replace("\\", "\\\\")}""
                }},
                ""data-source-files"": [""{childFilePath.Replace("\\", "\\\\")}""],
                ""runtime"": {{
                    ""rest"": {{ ""enabled"": true }},
                    ""graphql"": {{ ""enabled"": true }},
                    ""host"": {{
                        ""cors"": {{ ""origins"": [] }},
                        ""authentication"": {{ ""provider"": ""StaticWebApps"" }}
                    }}
                }},
                ""entities"": {{}}
            }}";

            // Child config with its own azure-key-vault pointing to the child's .akv file,
            // and a connection string referencing a secret only in the child's vault.
            string childConfig = $@"{{
                ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
                ""data-source"": {{
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""@akv('child-connection-secret')""
                }},
                ""azure-key-vault"": {{
                    ""endpoint"": ""{childAkvFilePath.Replace("\\", "\\\\")}""
                }},
                ""runtime"": {{
                    ""rest"": {{ ""enabled"": true }},
                    ""graphql"": {{ ""enabled"": true }},
                    ""host"": {{
                        ""cors"": {{ ""origins"": [] }},
                        ""authentication"": {{ ""provider"": ""StaticWebApps"" }}
                    }}
                }},
                ""entities"": {{
                    ""ChildOverrideEntity"": {{
                        ""source"": ""dbo.ChildTable"",
                        ""permissions"": [{{ ""role"": ""anonymous"", ""actions"": [""read""] }}]
                    }}
                }}
            }}";

            await File.WriteAllTextAsync(childFilePath, childConfig);

            MockFileSystem fs = new(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(parentConfig) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);

            DeserializationVariableReplacementSettings replacementSettings = new(
                azureKeyVaultOptions: new AzureKeyVaultOptions() { Endpoint = parentAkvFilePath, UserProvidedEndpoint = true },
                doReplaceEnvVar: true,
                doReplaceAkvVar: true,
                envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore);

            Assert.IsTrue(
                loader.TryLoadConfig("dab-config.json", out RuntimeConfig runtimeConfig, replacementSettings: replacementSettings),
                "Config should load successfully when child config has its own AKV options.");

            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("ChildOverrideEntity"), "Child config entity should be merged into the parent config.");

            // Verify the child's connection string was resolved using the child's AKV file, not the parent's.
            string childDataSourceName = runtimeConfig.GetDataSourceNameFromEntityName("ChildOverrideEntity");
            DataSource childDataSource = runtimeConfig.GetDataSourceFromDataSourceName(childDataSourceName);
            Assert.IsTrue(
                childDataSource.ConnectionString.Contains("10.0.0.1"),
                "Child config connection string should be resolved from the child's own AKV file, not the parent's.");
        }
        finally
        {
            if (File.Exists(parentAkvFilePath))
            {
                File.Delete(parentAkvFilePath);
            }

            if (File.Exists(childAkvFilePath))
            {
                File.Delete(childAkvFilePath);
            }

            if (File.Exists(childFilePath))
            {
                File.Delete(childFilePath);
            }
        }
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
        string origEndpoint = Environment.GetEnvironmentVariable("NONEXISTENT_OTEL_ENDPOINT");
        string origHeaders = Environment.GetEnvironmentVariable("NONEXISTENT_OTEL_HEADERS");
        string origServiceName = Environment.GetEnvironmentVariable("NONEXISTENT_OTEL_SERVICE_NAME");
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
    /// Validates that when a child config file exists but contains invalid content,
    /// the parent config loading fails instead of silently skipping the child.
    /// Non-existent child files are intentionally skipped to support late-configured scenarios.
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3271
    /// </summary>
    [TestMethod]
    public async Task ChildConfigLoadFailureHaltsParentConfigLoading()
    {
        string parentConfig = await File.ReadAllTextAsync("Multidab-config.MsSql.json");

        // Use a real temp file with invalid JSON so the file exists but fails to parse.
        string invalidChildPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        try
        {
            await File.WriteAllTextAsync(invalidChildPath, "{ this is not valid json }");

            JObject parentJson = JObject.Parse(parentConfig);
            parentJson.Add("data-source-files", new JArray(invalidChildPath));
            string parentJsonStr = parentJson.ToString();

            MockFileSystem fs = new(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(parentJsonStr) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);

            TextWriter originalError = Console.Error;
            StringWriter sw = new();

            try
            {
                Console.SetError(sw);

                bool loaded = loader.TryLoadConfig("dab-config.json", out RuntimeConfig _);
                string error = sw.ToString();

                Assert.IsFalse(loaded, "Config loading should fail when a child config file exists but cannot be parsed.");
                Assert.IsTrue(error.Contains("Failed to load datasource file"), "Error message should indicate the child config file that failed to load.");
            }
            finally
            {
                Console.SetError(originalError);
                sw.Dispose();
            }
        }
        finally
        {
            if (File.Exists(invalidChildPath))
            {
                File.Delete(invalidChildPath);
            }
        }
    }

    /// <summary>
    /// Tests that EnableAggregation returns true by default when runtime.graphql section is absent.
    /// This is a regression test for the bug where EnableAggregation returned false (disabled)
    /// when Runtime.GraphQL was null, even though the default value for EnableAggregation is true.
    /// </summary>
    [TestMethod]
    public void EnableAggregation_WhenGraphQLSectionAbsent_DefaultsToTrue()
    {
        // Arrange: a minimal config with no runtime.graphql section
        string configJson = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=tcp:127.0.0.1,1433;""
            },
            ""runtime"": {
                ""host"": {
                    ""authentication"": { ""provider"": ""StaticWebApps"" }
                }
            },
            ""entities"": {}
        }";

        RuntimeConfig runtimeConfig = LoadConfig(configJson);

        Assert.IsNull(runtimeConfig.Runtime?.GraphQL, "GraphQL section should be null for this config.");
        Assert.IsTrue(runtimeConfig.EnableAggregation,
            "EnableAggregation should default to true when runtime.graphql section is absent.");
    }

    /// <summary>
    /// Tests that EnableAggregation returns true by default when runtime section is absent.
    /// </summary>
    [TestMethod]
    public void EnableAggregation_WhenRuntimeSectionAbsent_DefaultsToTrue()
    {
        // Arrange: a minimal config with no runtime section at all
        string configJson = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=tcp:127.0.0.1,1433;""
            },
            ""entities"": {}
        }";

        RuntimeConfig runtimeConfig = LoadConfig(configJson);

        Assert.IsNull(runtimeConfig.Runtime, "Runtime section should be null for this config.");
        Assert.IsTrue(runtimeConfig.EnableAggregation,
            "EnableAggregation should default to true when runtime section is absent.");
    }

    /// <summary>
    /// Tests that EnableAggregation honours the explicit value set in the config file.
    /// </summary>
    [DataTestMethod]
    [DataRow(true, DisplayName = "Explicit true is respected")]
    [DataRow(false, DisplayName = "Explicit false is respected")]
    public void EnableAggregation_WhenExplicitlySet_ReturnsConfiguredValue(bool explicitValue)
    {
        string configJson = $@"{{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {{
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=tcp:127.0.0.1,1433;""
            }},
            ""runtime"": {{
                ""graphql"": {{
                    ""enabled"": true,
                    ""enable-aggregation"": {explicitValue.ToString().ToLower()}
                }},
                ""host"": {{
                    ""authentication"": {{ ""provider"": ""StaticWebApps"" }}
                }}
            }},
            ""entities"": {{}}
        }}";

        RuntimeConfig runtimeConfig = LoadConfig(configJson);

        Assert.AreEqual(explicitValue, runtimeConfig.EnableAggregation,
            $"EnableAggregation should be {explicitValue} when explicitly set to {explicitValue} in config.");
    }

    /// <summary>
    /// Loads a <see cref="RuntimeConfig"/> from a JSON string using a mock file system.
    /// </summary>
    private static RuntimeConfig LoadConfig(string configJson)
    {
        IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { "dab-config.json", new MockFileData(configJson) }
        });

        FileSystemRuntimeConfigLoader loader = new(fs);
        Assert.IsTrue(loader.TryLoadConfig("dab-config.json", out RuntimeConfig config), "Config should load successfully.");
        return config;
    }
}

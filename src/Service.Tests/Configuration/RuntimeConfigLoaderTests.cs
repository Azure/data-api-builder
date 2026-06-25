// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.Logging;
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

        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Error;
            });
        });
        ILogger<FileSystemRuntimeConfigLoader> logger = loggerFactory.CreateLogger<FileSystemRuntimeConfigLoader>();

        loader.SetLogger(logger);
        loader.TryLoadConfig("dab-config.json", out RuntimeConfig _);

        await TestHelper.DelayTask(() => string.IsNullOrWhiteSpace(sw.ToString()));

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

                ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Trace);
                    builder.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Error;
                    });
                });
                ILogger<FileSystemRuntimeConfigLoader> logger = loggerFactory.CreateLogger<FileSystemRuntimeConfigLoader>();

                loader.SetLogger(logger);
                bool loaded = loader.TryLoadConfig("dab-config.json", out RuntimeConfig _);
                await TestHelper.DelayTask(() => string.IsNullOrWhiteSpace(sw.ToString()));
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
    /// In a multi-database setup, the connection-string "Application Name" (anonymous usage telemetry)
    /// for a child data source must reflect the GLOBAL runtime settings and the merged entity set — not
    /// the child config's own (absent) runtime. Child configs defer Application Name injection to the
    /// top-level load, which performs it once over the fully-merged config so every connection pool
    /// carries a self-contained snapshot of the deployment.
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3216
    /// </summary>
    [TestMethod]
    public async Task MultiDbChildDataSourceConnectionStringEncodesGlobalTelemetry()
    {
        // Root config: defines the GLOBAL runtime (REST on, GraphQL off, StaticWebApps auth) plus its
        // own default MSSQL data source. GraphQL-off / StaticWebApps make the runtime section distinctive.
        string parentConfig = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=tcp:127.0.0.1,1433;Database=ParentDb;TrustServerCertificate=True;""
            },
            ""runtime"": {
                ""rest"": { ""enabled"": true },
                ""graphql"": { ""enabled"": false },
                ""host"": {
                    ""cors"": { ""origins"": [] },
                    ""authentication"": { ""provider"": ""StaticWebApps"" }
                }
            },
            ""entities"": {
                ""ParentEntity"": {
                    ""source"": ""dbo.ParentTable"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""read""] }]
                }
            }
        }";

        // Child config: a second MSSQL data source with NO runtime section of its own. Before the fix,
        // its telemetry was computed from this partial config and the runtime section was all-missing.
        string childConfig = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=tcp:127.0.0.1,1433;Database=ChildDb;TrustServerCertificate=True;""
            },
            ""entities"": {
                ""ChildEntity"": {
                    ""source"": ""dbo.ChildTable"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""read""] }]
                }
            }
        }";

        // The RuntimeConfig constructor loads child data-source-files from a real FileSystem, so the
        // child config must live in a real temp file.
        string childFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            await File.WriteAllTextAsync(childFilePath, childConfig);

            JObject parentJson = JObject.Parse(parentConfig);
            parentJson.Add("data-source-files", new JArray(childFilePath));

            MockFileSystem fs = new(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(parentJson.ToString()) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);

            DeserializationVariableReplacementSettings replacementSettings = new(
                azureKeyVaultOptions: null,
                doReplaceEnvVar: true,
                doReplaceAkvVar: false,
                envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore);

            Assert.IsTrue(
                loader.TryLoadConfig("dab-config.json", out RuntimeConfig runtimeConfig, replacementSettings: replacementSettings),
                "Multi-database config should load successfully.");

            // Resolve both data sources from the merged config.
            DataSource parentDataSource = runtimeConfig.GetDataSourceFromDataSourceName(runtimeConfig.GetDataSourceNameFromEntityName("ParentEntity"));
            DataSource childDataSource = runtimeConfig.GetDataSourceFromDataSourceName(runtimeConfig.GetDataSourceNameFromEntityName("ChildEntity"));

            (_, string parentRuntime, string parentEntity) = GetTelemetrySections(parentDataSource.ConnectionString);
            (_, string childRuntime, string childEntity) = GetTelemetrySections(childDataSource.ConnectionString);

            // Sanity: the root has a real runtime, so its encoded runtime section is meaningful, i.e. not
            // entirely the 'M' (missing) sentinel. This guarantees the equality checks below are meaningful.
            Assert.IsTrue(
                parentRuntime.Any(flag => flag != 'M'),
                $"Root runtime telemetry section should be meaningful, but was all-missing: '{parentRuntime}'.");

            // The fix: a child data source with no runtime of its own must encode the GLOBAL runtime,
            // identical to the default data source's pool.
            Assert.AreEqual(
                parentRuntime,
                childRuntime,
                "Child data source telemetry should encode the global runtime, identical to the default data source.");

            // Entities are global (merged), so every pool encodes the same entity section.
            Assert.AreEqual(
                parentEntity,
                childEntity,
                "Child data source telemetry should encode the merged (global) entity section, identical to the default data source.");
        }
        finally
        {
            if (File.Exists(childFilePath))
            {
                File.Delete(childFilePath);
            }
        }
    }

    /// <summary>
    /// Heterogeneous multi-database setup: a PostgreSQL child data source must also embed usage telemetry
    /// in its connection-string "Application Name", encoding the GLOBAL runtime + merged entities (identical
    /// to the MSSQL default pool) while reporting its own Source character ('P'). Companion to the MSSQL
    /// multi-DB test; guards the Postgres extension of the telemetry feature.
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3216
    /// </summary>
    [TestMethod]
    public async Task MultiDbPostgresChildDataSourceEncodesGlobalTelemetryWithPostgresSource()
    {
        // Root (default) MSSQL data source carrying the GLOBAL runtime.
        string parentConfig = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=tcp:127.0.0.1,1433;Database=ParentDb;TrustServerCertificate=True;""
            },
            ""runtime"": {
                ""rest"": { ""enabled"": true },
                ""graphql"": { ""enabled"": false },
                ""host"": {
                    ""cors"": { ""origins"": [] },
                    ""authentication"": { ""provider"": ""StaticWebApps"" }
                }
            },
            ""entities"": {
                ""ParentEntity"": {
                    ""source"": ""dbo.ParentTable"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""read""] }]
                }
            }
        }";

        // Child PostgreSQL data source with NO runtime section of its own.
        string childConfig = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""postgresql"",
                ""connection-string"": ""Host=localhost;Database=ChildDb;Username=testuser;""
            },
            ""entities"": {
                ""ChildEntity"": {
                    ""source"": ""public.ChildTable"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""read""] }]
                }
            }
        }";

        // The RuntimeConfig constructor loads child data-source-files from a real FileSystem, so the
        // child config must live in a real temp file.
        string childFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            await File.WriteAllTextAsync(childFilePath, childConfig);

            JObject parentJson = JObject.Parse(parentConfig);
            parentJson.Add("data-source-files", new JArray(childFilePath));

            MockFileSystem fs = new(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(parentJson.ToString()) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);

            DeserializationVariableReplacementSettings replacementSettings = new(
                azureKeyVaultOptions: null,
                doReplaceEnvVar: true,
                doReplaceAkvVar: false,
                envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore);

            Assert.IsTrue(
                loader.TryLoadConfig("dab-config.json", out RuntimeConfig runtimeConfig, replacementSettings: replacementSettings),
                "Heterogeneous multi-database config should load successfully.");

            DataSource parentDataSource = runtimeConfig.GetDataSourceFromDataSourceName(runtimeConfig.GetDataSourceNameFromEntityName("ParentEntity"));
            DataSource childDataSource = runtimeConfig.GetDataSourceFromDataSourceName(runtimeConfig.GetDataSourceNameFromEntityName("ChildEntity"));

            (string parentContext, string parentRuntime, string parentEntity) = GetTelemetrySections(parentDataSource.ConnectionString);
            (string childContext, string childRuntime, string childEntity) = GetTelemetrySections(childDataSource.ConnectionString);

            // Context = [Protocol][Object][Source][Role]; only Source is known at pool time.
            // The PostgreSQL pool encodes Source 'P'; the MSSQL pool encodes Source 'S'.
            Assert.AreEqual('P', childContext[2], $"PostgreSQL data source should encode Source 'P'. Actual context: '{childContext}'.");
            Assert.AreEqual('S', parentContext[2], $"MSSQL data source should encode Source 'S'. Actual context: '{parentContext}'.");

            // Runtime and entities are global, so both pools (regardless of engine) encode identical sections.
            Assert.AreEqual(
                parentRuntime,
                childRuntime,
                "PostgreSQL child should encode the global runtime, identical to the MSSQL default data source.");
            Assert.AreEqual(
                parentEntity,
                childEntity,
                "PostgreSQL child should encode the merged (global) entity section, identical to the MSSQL default data source.");
        }
        finally
        {
            if (File.Exists(childFilePath))
            {
                File.Delete(childFilePath);
            }
        }
    }

    /// <summary>
    /// Extracts the three telemetry sections (context, runtime, entity) from the DAB usage-telemetry
    /// payload embedded in a connection string's "Application Name" property.
    /// Payload shape: [{env},]dab_oss_&lt;version&gt;+&lt;context&gt;|&lt;runtime&gt;|&lt;entity&gt;+
    /// </summary>
    private static (string Context, string Runtime, string Entity) GetTelemetrySections(string connectionString)
    {
        // Use the engine-agnostic base builder so this works for both SQL Server and PostgreSQL connection strings.
        DbConnectionStringBuilder builder = new() { ConnectionString = connectionString };
        Assert.IsTrue(
            builder.TryGetValue("Application Name", out object applicationNameValue),
            $"Connection string '{connectionString}' should contain an Application Name.");
        string applicationName = (string)applicationNameValue;

        Assert.IsTrue(
            applicationName.Contains(ProductInfo.DAB_USER_AGENT_MARKER) && applicationName.EndsWith("+", StringComparison.Ordinal),
            $"Application Name '{applicationName}' should carry a DAB telemetry payload ending with '+'.");

        // Drop the trailing delimiter, then take the region after the last '+' (the version itself can
        // contain '+' build metadata, so anchoring on the last '+' before the sections is robust).
        string sectionsRegion = applicationName.TrimEnd('+');
        sectionsRegion = sectionsRegion.Substring(sectionsRegion.LastIndexOf('+') + 1);

        string[] sections = sectionsRegion.Split('|');
        Assert.AreEqual(3, sections.Length, $"Telemetry payload in '{applicationName}' should have 3 sections, but was '{sectionsRegion}'.");

        return (sections[0], sections[1], sections[2]);
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
    /// Embedding telemetry into a connection string that already carries the DAB telemetry marker must
    /// be a no-op (idempotent), so a value can never accumulate a duplicated payload
    /// (<c>...+...+,dab_oss_...+...+</c>) if the embed ever runs more than once (e.g. the loader's
    /// post-processing followed by the late-config provider).
    /// </summary>
    [TestMethod]
    public void GetConnectionStringWithApplicationName_IsIdempotent()
    {
        DataSource dataSource = new(DatabaseType.MSSQL, "Server=localhost;Database=test;");
        RuntimeConfig config = new(
            Schema: "s",
            DataSource: dataSource,
            Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

        string once = RuntimeConfigLoader.GetConnectionStringWithApplicationName("Server=localhost;Database=test;", config, dataSource);
        string twice = RuntimeConfigLoader.GetConnectionStringWithApplicationName(once, config, dataSource);

        Assert.AreEqual(once, twice, "Re-embedding telemetry should be a no-op (idempotent).");

        int markerOccurrences = (twice.Length - twice.Replace(ProductInfo.DAB_USER_AGENT_MARKER, string.Empty).Length) / ProductInfo.DAB_USER_AGENT_MARKER.Length;
        Assert.AreEqual(1, markerOccurrences, $"Telemetry marker should appear exactly once but was '{twice}'.");
    }

    /// <summary>
    /// Computing the telemetry Application Name buffers a Debug log into a shared static buffer that,
    /// before the fix, was only drained once at startup — so the log was never emitted on hot reload and
    /// the buffer accumulated an entry per data source on every reload. This validates that
    /// <see cref="FileSystemRuntimeConfigLoader.FlushLogBuffer"/> (a) is null-safe when no logger has been
    /// set, and (b) emits the buffered telemetry log to a configured logger (so reloads drain the buffer).
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3216
    /// </summary>
    [TestMethod]
    public void FlushLogBuffer_IsNullSafe_AndEmitsBufferedTelemetryLog()
    {
        DataSource dataSource = new(DatabaseType.MSSQL, "Server=localhost;Database=test;");
        RuntimeConfig config = new(
            Schema: "s",
            DataSource: dataSource,
            Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

        // Computing the Application Name buffers a Debug telemetry log into the shared static buffer.
        RuntimeConfigLoader.GetConnectionStringWithApplicationName("Server=localhost;Database=test;", config, dataSource);

        // (a) A loader with no logger set must not throw when flushing a non-empty buffer. Previously this
        // threw a NullReferenceException because the buffer was flushed to a null logger.
        FileSystemRuntimeConfigLoader loaderWithoutLogger = new(new MockFileSystem());
        loaderWithoutLogger.FlushLogBuffer();

        // (b) With a logger set, a freshly buffered telemetry log is emitted rather than silently
        // accumulating in the static buffer until the next startup.
        RuntimeConfigLoader.GetConnectionStringWithApplicationName("Server=localhost;Database=test;", config, dataSource);

        CapturingLogger<FileSystemRuntimeConfigLoader> logger = new();
        FileSystemRuntimeConfigLoader loaderWithLogger = new(new MockFileSystem());
        loaderWithLogger.SetLogger(logger);
        loaderWithLogger.FlushLogBuffer();

        Assert.IsTrue(
            logger.Messages.Any(m => m.Contains("DAB telemetry Application Name computed")),
            "FlushLogBuffer should emit the buffered telemetry Debug log to the configured logger.");
    }

    /// <summary>Minimal in-memory <see cref="ILogger{T}"/> that records formatted messages for assertions.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
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

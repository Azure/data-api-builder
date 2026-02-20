// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for EmbeddingsOptions deserialization and EmbeddingProviderType enum.
/// </summary>
[TestClass]
public class EmbeddingsOptionsTests
{
    private const string BASIC_CONFIG_WITH_EMBEDDINGS = @"
    {
        ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
        ""data-source"": {
            ""database-type"": ""mssql"",
            ""connection-string"": ""Server=test;Database=test;""
        },
        ""runtime"": {
            ""embeddings"": {
                ""provider"": ""azure-openai"",
                ""base-url"": ""https://my-openai.openai.azure.com"",
                ""api-key"": ""test-api-key"",
                ""model"": ""text-embedding-ada-002"",
                ""api-version"": ""2024-02-01"",
                ""dimensions"": 1536,
                ""timeout-ms"": 30000
            }
        },
        ""entities"": {}
    }";

    private const string OPENAI_CONFIG = @"
    {
        ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
        ""data-source"": {
            ""database-type"": ""mssql"",
            ""connection-string"": ""Server=test;Database=test;""
        },
        ""runtime"": {
            ""embeddings"": {
                ""provider"": ""openai"",
                ""base-url"": ""https://api.openai.com"",
                ""api-key"": ""sk-test-key""
            }
        },
        ""entities"": {}
    }";

    private const string MINIMAL_AZURE_CONFIG = @"
    {
        ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
        ""data-source"": {
            ""database-type"": ""mssql"",
            ""connection-string"": ""Server=test;Database=test;""
        },
        ""runtime"": {
            ""embeddings"": {
                ""provider"": ""azure-openai"",
                ""base-url"": ""https://my-openai.openai.azure.com"",
                ""api-key"": ""test-api-key"",
                ""model"": ""my-deployment""
            }
        },
        ""entities"": {}
    }";

    private const string CONFIG_WITHOUT_EMBEDDINGS = @"
    {
        ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
        ""data-source"": {
            ""database-type"": ""mssql"",
            ""connection-string"": ""Server=test;Database=test;""
        },
        ""entities"": {}
    }";

    /// <summary>
    /// Tests that Azure OpenAI embeddings configuration deserializes correctly.
    /// </summary>
    [TestMethod]
    public void TestAzureOpenAIEmbeddingsConfigDeserialization()
    {
        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(BASIC_CONFIG_WITH_EMBEDDINGS, out RuntimeConfig? runtimeConfig);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(runtimeConfig);
        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.IsNotNull(runtimeConfig.Runtime.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        Assert.AreEqual(EmbeddingProviderType.AzureOpenAI, embeddings.Provider);
        Assert.AreEqual("https://my-openai.openai.azure.com", embeddings.BaseUrl);
        Assert.AreEqual("test-api-key", embeddings.ApiKey);
        Assert.AreEqual("text-embedding-ada-002", embeddings.Model);
        Assert.AreEqual("2024-02-01", embeddings.ApiVersion);
        Assert.AreEqual(1536, embeddings.Dimensions);
        Assert.AreEqual(30000, embeddings.TimeoutMs);
    }

    /// <summary>
    /// Tests that OpenAI embeddings configuration deserializes correctly with defaults.
    /// </summary>
    [TestMethod]
    public void TestOpenAIEmbeddingsConfigWithDefaults()
    {
        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(OPENAI_CONFIG, out RuntimeConfig? runtimeConfig);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        Assert.AreEqual(EmbeddingProviderType.OpenAI, embeddings.Provider);
        Assert.AreEqual("https://api.openai.com", embeddings.BaseUrl);
        Assert.AreEqual("sk-test-key", embeddings.ApiKey);
        Assert.IsNull(embeddings.Model);
        Assert.AreEqual(EmbeddingsOptions.DEFAULT_OPENAI_MODEL, embeddings.EffectiveModel);
        Assert.IsNull(embeddings.ApiVersion);
        Assert.IsNull(embeddings.Dimensions);
        Assert.IsNull(embeddings.TimeoutMs);
        Assert.AreEqual(EmbeddingsOptions.DEFAULT_TIMEOUT_MS, embeddings.EffectiveTimeoutMs);
    }

    /// <summary>
    /// Tests that minimal Azure OpenAI config deserializes correctly.
    /// </summary>
    [TestMethod]
    public void TestMinimalAzureOpenAIConfig()
    {
        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(MINIMAL_AZURE_CONFIG, out RuntimeConfig? runtimeConfig);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        Assert.AreEqual(EmbeddingProviderType.AzureOpenAI, embeddings.Provider);
        Assert.AreEqual("my-deployment", embeddings.Model);
        Assert.AreEqual("my-deployment", embeddings.EffectiveModel);
    }

    /// <summary>
    /// Tests that configuration without embeddings section deserializes correctly.
    /// </summary>
    [TestMethod]
    public void TestConfigWithoutEmbeddings()
    {
        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(CONFIG_WITHOUT_EMBEDDINGS, out RuntimeConfig? runtimeConfig);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(runtimeConfig);
        Assert.IsNull(runtimeConfig.Runtime?.Embeddings);
    }

    /// <summary>
    /// Tests that EmbeddingProviderType enum deserializes correctly from JSON.
    /// </summary>
    [DataTestMethod]
    [DataRow("azure-openai", EmbeddingProviderType.AzureOpenAI)]
    [DataRow("openai", EmbeddingProviderType.OpenAI)]
    public void TestEmbeddingProviderTypeDeserialization(string jsonValue, EmbeddingProviderType expected)
    {
        // Arrange
        string config = $@"
        {{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {{
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=test;Database=test;""
            }},
            ""runtime"": {{
                ""embeddings"": {{
                    ""provider"": ""{jsonValue}"",
                    ""base-url"": ""https://example.com"",
                    ""api-key"": ""test-key"",
                    ""model"": ""test-model""
                }}
            }},
            ""entities"": {{}}
        }}";

        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig? runtimeConfig);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);
        Assert.AreEqual(expected, runtimeConfig.Runtime.Embeddings.Provider);
    }

    /// <summary>
    /// Tests EmbeddingsOptions serialization to JSON.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsOptionsSerialization()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://my-endpoint.openai.azure.com",
            ApiKey: "my-api-key",
            Model: "my-model",
            ApiVersion: "2024-02-01",
            Dimensions: 1536,
            TimeoutMs: 60000);

        // Act
        JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions(replacementSettings: null);
        string json = JsonSerializer.Serialize(options, serializerOptions);

        // Normalize json for comparison (remove whitespace)
        string normalizedJson = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");

        // Assert
        Assert.IsTrue(normalizedJson.Contains("\"provider\":\"azure-openai\""), $"Expected provider in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"base-url\":\"https://my-endpoint.openai.azure.com\""), $"Expected base-url in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"api-key\":\"my-api-key\""), $"Expected api-key in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"model\":\"my-model\""), $"Expected model in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"api-version\":\"2024-02-01\""), $"Expected api-version in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"dimensions\":1536"), $"Expected dimensions in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"timeout-ms\":60000"), $"Expected timeout-ms in JSON: {json}");
    }

    /// <summary>
    /// Tests that environment variable replacement works for embeddings configuration.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsConfigWithEnvVarReplacement()
    {
        // Arrange
        string config = @"
        {
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=test;Database=test;""
            },
            ""runtime"": {
                ""embeddings"": {
                    ""provider"": ""azure-openai"",
                    ""base-url"": ""@env('EMBEDDINGS_ENDPOINT')"",
                    ""api-key"": ""@env('EMBEDDINGS_API_KEY')"",
                    ""model"": ""@env('EMBEDDINGS_MODEL')""
                }
            },
            ""entities"": {}
        }";

        // Set environment variables
        Environment.SetEnvironmentVariable("EMBEDDINGS_ENDPOINT", "https://test.openai.azure.com");
        Environment.SetEnvironmentVariable("EMBEDDINGS_API_KEY", "test-key-from-env");
        Environment.SetEnvironmentVariable("EMBEDDINGS_MODEL", "test-model-from-env");

        // Create replacement settings to enable env var replacement
        DeserializationVariableReplacementSettings replacementSettings = new(
            doReplaceEnvVar: true,
            doReplaceAkvVar: false,
            envFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

        try
        {
            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig? runtimeConfig, replacementSettings);

            // Assert
            Assert.IsTrue(success);
            Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

            EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
            Assert.AreEqual("https://test.openai.azure.com", embeddings.BaseUrl);
            Assert.AreEqual("test-key-from-env", embeddings.ApiKey);
            Assert.AreEqual("test-model-from-env", embeddings.Model);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("EMBEDDINGS_ENDPOINT", null);
            Environment.SetEnvironmentVariable("EMBEDDINGS_API_KEY", null);
            Environment.SetEnvironmentVariable("EMBEDDINGS_MODEL", null);
        }
    }
}

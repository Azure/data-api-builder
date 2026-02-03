// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
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
                ""endpoint"": ""https://my-openai.openai.azure.com"",
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
                ""endpoint"": ""https://api.openai.com"",
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
                ""endpoint"": ""https://my-openai.openai.azure.com"",
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
    /// Tests that a full Azure OpenAI embeddings configuration is correctly deserialized.
    /// </summary>
    [TestMethod]
    public void TestAzureOpenAIEmbeddingsConfigDeserialization()
    {
        // Act
        bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            BASIC_CONFIG_WITH_EMBEDDINGS,
            out RuntimeConfig runtimeConfig,
            replacementSettings: new DeserializationVariableReplacementSettings(
                azureKeyVaultOptions: null,
                doReplaceEnvVar: false,
                doReplaceAkvVar: false));

        // Assert
        Assert.IsTrue(isParsingSuccessful);
        Assert.IsNotNull(runtimeConfig);
        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.IsTrue(runtimeConfig.Runtime.IsEmbeddingsConfigured);
        Assert.IsNotNull(runtimeConfig.Runtime.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        Assert.AreEqual(EmbeddingProviderType.AzureOpenAI, embeddings.Provider);
        Assert.AreEqual("https://my-openai.openai.azure.com", embeddings.Endpoint);
        Assert.AreEqual("test-api-key", embeddings.ApiKey);
        Assert.AreEqual("text-embedding-ada-002", embeddings.Model);
        Assert.AreEqual("2024-02-01", embeddings.ApiVersion);
        Assert.AreEqual(1536, embeddings.Dimensions);
        Assert.AreEqual(30000, embeddings.TimeoutMs);

        // Verify UserProvided flags
        Assert.IsTrue(embeddings.UserProvidedModel);
        Assert.IsTrue(embeddings.UserProvidedApiVersion);
        Assert.IsTrue(embeddings.UserProvidedDimensions);
        Assert.IsTrue(embeddings.UserProvidedTimeoutMs);
    }

    /// <summary>
    /// Tests that an OpenAI embeddings configuration without optional fields is correctly deserialized
    /// and default values are applied.
    /// </summary>
    [TestMethod]
    public void TestOpenAIEmbeddingsConfigWithDefaults()
    {
        // Act
        bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            OPENAI_CONFIG,
            out RuntimeConfig runtimeConfig,
            replacementSettings: new DeserializationVariableReplacementSettings(
                azureKeyVaultOptions: null,
                doReplaceEnvVar: false,
                doReplaceAkvVar: false));

        // Assert
        Assert.IsTrue(isParsingSuccessful);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        Assert.AreEqual(EmbeddingProviderType.OpenAI, embeddings.Provider);
        Assert.AreEqual("https://api.openai.com", embeddings.Endpoint);
        Assert.AreEqual("sk-test-key", embeddings.ApiKey);

        // Model not specified, but EffectiveModel should return default for OpenAI
        Assert.IsNull(embeddings.Model);
        Assert.AreEqual(EmbeddingsOptions.DEFAULT_OPENAI_MODEL, embeddings.EffectiveModel);

        // Optional fields should use effective defaults
        Assert.AreEqual(EmbeddingsOptions.DEFAULT_TIMEOUT_MS, embeddings.EffectiveTimeoutMs);
        Assert.AreEqual(EmbeddingsOptions.DEFAULT_AZURE_API_VERSION, embeddings.EffectiveApiVersion);

        // UserProvided flags should be false for optional fields
        Assert.IsFalse(embeddings.UserProvidedModel);
        Assert.IsFalse(embeddings.UserProvidedApiVersion);
        Assert.IsFalse(embeddings.UserProvidedDimensions);
        Assert.IsFalse(embeddings.UserProvidedTimeoutMs);
    }

    /// <summary>
    /// Tests minimal Azure OpenAI configuration with required fields only.
    /// </summary>
    [TestMethod]
    public void TestMinimalAzureOpenAIConfig()
    {
        // Act
        bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            MINIMAL_AZURE_CONFIG,
            out RuntimeConfig runtimeConfig,
            replacementSettings: new DeserializationVariableReplacementSettings(
                azureKeyVaultOptions: null,
                doReplaceEnvVar: false,
                doReplaceAkvVar: false));

        // Assert
        Assert.IsTrue(isParsingSuccessful);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        Assert.AreEqual(EmbeddingProviderType.AzureOpenAI, embeddings.Provider);
        Assert.AreEqual("my-deployment", embeddings.Model);
        Assert.AreEqual("my-deployment", embeddings.EffectiveModel);
        Assert.IsTrue(embeddings.UserProvidedModel);
    }

    /// <summary>
    /// Tests that a configuration without embeddings returns IsEmbeddingsConfigured as false.
    /// </summary>
    [TestMethod]
    public void TestConfigWithoutEmbeddings()
    {
        // Act
        bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            CONFIG_WITHOUT_EMBEDDINGS,
            out RuntimeConfig runtimeConfig,
            replacementSettings: new DeserializationVariableReplacementSettings(
                azureKeyVaultOptions: null,
                doReplaceEnvVar: false,
                doReplaceAkvVar: false));

        // Assert
        Assert.IsTrue(isParsingSuccessful);
        Assert.IsNotNull(runtimeConfig);

        // Runtime may be null or Embeddings may be null
        bool isEmbeddingsConfigured = runtimeConfig.Runtime?.IsEmbeddingsConfigured ?? false;
        Assert.IsFalse(isEmbeddingsConfigured);
    }

    /// <summary>
    /// Tests that EmbeddingProviderType enum is correctly serialized with kebab-case.
    /// </summary>
    [DataTestMethod]
    [DataRow("azure-openai", EmbeddingProviderType.AzureOpenAI, DisplayName = "azure-openai deserializes to AzureOpenAI")]
    [DataRow("openai", EmbeddingProviderType.OpenAI, DisplayName = "openai deserializes to OpenAI")]
    public void TestEmbeddingProviderTypeDeserialization(string providerValue, EmbeddingProviderType expectedType)
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
                    ""provider"": ""{providerValue}"",
                    ""endpoint"": ""https://example.com"",
                    ""api-key"": ""test-key"",
                    ""model"": ""test-model""
                }}
            }},
            ""entities"": {{}}
        }}";

        // Act
        bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
            config,
            out RuntimeConfig runtimeConfig,
            replacementSettings: new DeserializationVariableReplacementSettings(
                azureKeyVaultOptions: null,
                doReplaceEnvVar: false,
                doReplaceAkvVar: false));

        // Assert
        Assert.IsTrue(isParsingSuccessful);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);
        Assert.AreEqual(expectedType, runtimeConfig.Runtime.Embeddings.Provider);
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
            Endpoint: "https://my-endpoint.openai.azure.com",
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
        Assert.IsTrue(normalizedJson.Contains("\"endpoint\":\"https://my-endpoint.openai.azure.com\""), $"Expected endpoint in JSON: {json}");
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
                    ""endpoint"": ""@env('EMBEDDINGS_ENDPOINT')"",
                    ""api-key"": ""@env('EMBEDDINGS_API_KEY')"",
                    ""model"": ""@env('EMBEDDINGS_MODEL')""
                }
            },
            ""entities"": {}
        }";

        // Set environment variables
        Environment.SetEnvironmentVariable("EMBEDDINGS_ENDPOINT", "https://test-endpoint.openai.azure.com");
        Environment.SetEnvironmentVariable("EMBEDDINGS_API_KEY", "test-secret-key");
        Environment.SetEnvironmentVariable("EMBEDDINGS_MODEL", "text-embedding-3-small");

        try
        {
            // Act
            bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
                config,
                out RuntimeConfig runtimeConfig,
                replacementSettings: new DeserializationVariableReplacementSettings(
                    azureKeyVaultOptions: null,
                    doReplaceEnvVar: true,
                    doReplaceAkvVar: false));

            // Assert
            Assert.IsTrue(isParsingSuccessful);
            Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

            EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
            Assert.AreEqual("https://test-endpoint.openai.azure.com", embeddings.Endpoint);
            Assert.AreEqual("test-secret-key", embeddings.ApiKey);
            Assert.AreEqual("text-embedding-3-small", embeddings.Model);
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

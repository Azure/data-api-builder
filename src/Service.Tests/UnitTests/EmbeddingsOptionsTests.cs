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

    /// <summary>
    /// Tests that chunking configuration deserializes correctly.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsOptionsWithChunkingDeserialization()
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
                    ""base-url"": ""https://test.openai.azure.com"",
                    ""api-key"": ""test-key"",
                    ""model"": ""test-model"",
                    ""chunking"": {
                        ""enabled"": true,
                        ""size-chars"": 1000,
                        ""overlap-chars"": 250
                    }
                }
            },
            ""entities"": {}
        }";

        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig? runtimeConfig);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        Assert.IsNotNull(embeddings.Chunking);
        Assert.IsTrue(embeddings.Chunking.Enabled);
        Assert.AreEqual(1000, embeddings.Chunking.SizeChars);
        Assert.AreEqual(250, embeddings.Chunking.OverlapChars);
    }

    /// <summary>
    /// Tests that chunking property is preserved during serialization.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsOptionsSerializationWithChunking()
    {
        // Arrange
        EmbeddingsChunkingOptions chunkingOptions = new(
            enabled: true,
            sizeChars: 1000,
            overlapChars: 250);

        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "test-key",
            Model: "test-model",
            Chunking: chunkingOptions);

        // Act
        JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions(replacementSettings: null);
        string json = JsonSerializer.Serialize(options, serializerOptions);

        // Normalize json for comparison
        string normalizedJson = json.Replace(" ", "").Replace("\n", "").Replace("\r", "");

        // Assert
        Assert.IsTrue(normalizedJson.Contains("\"chunking\":{"), $"Expected chunking object in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"enabled\":true"), $"Expected chunking.enabled in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"size-chars\":1000"), $"Expected chunking.size-chars in JSON: {json}");
        Assert.IsTrue(normalizedJson.Contains("\"overlap-chars\":250"), $"Expected chunking.overlap-chars in JSON: {json}");
    }

    /// <summary>
    /// Tests round-trip serialization preserves all properties including chunking.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsOptionsRoundTripSerialization()
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
                    ""enabled"": true,
                    ""provider"": ""azure-openai"",
                    ""base-url"": ""https://test.openai.azure.com"",
                    ""api-key"": ""test-key"",
                    ""model"": ""test-model"",
                    ""api-version"": ""2024-02-01"",
                    ""dimensions"": 1536,
                    ""timeout-ms"": 30000,
                    ""endpoint"": {
                        ""enabled"": true,
                        ""roles"": [""authenticated"", ""anonymous""]
                    },
                    ""health"": {
                        ""enabled"": true,
                        ""threshold-ms"": 5000,
                        ""test-text"": ""test embedding"",
                        ""expected-dimensions"": 1536
                    },
                    ""chunking"": {
                        ""enabled"": true,
                        ""size-chars"": 1000,
                        ""overlap-chars"": 250
                    }
                }
            },
            ""entities"": {}
        }";

        // Act - First deserialization
        bool success1 = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig? runtimeConfig1);
        Assert.IsTrue(success1);
        Assert.IsNotNull(runtimeConfig1?.Runtime?.Embeddings);

        // Serialize
        JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions(replacementSettings: null);
        string serializedJson = JsonSerializer.Serialize(runtimeConfig1, serializerOptions);

        // Second deserialization
        bool success2 = RuntimeConfigLoader.TryParseConfig(serializedJson, out RuntimeConfig? runtimeConfig2);
        Assert.IsTrue(success2);
        Assert.IsNotNull(runtimeConfig2?.Runtime?.Embeddings);

        // Assert - Verify all properties match
        EmbeddingsOptions original = runtimeConfig1.Runtime.Embeddings;
        EmbeddingsOptions roundTripped = runtimeConfig2.Runtime.Embeddings;

        Assert.AreEqual(original.Enabled, roundTripped.Enabled);
        Assert.AreEqual(original.Provider, roundTripped.Provider);
        Assert.AreEqual(original.BaseUrl, roundTripped.BaseUrl);
        Assert.AreEqual(original.ApiKey, roundTripped.ApiKey);
        Assert.AreEqual(original.Model, roundTripped.Model);
        Assert.AreEqual(original.ApiVersion, roundTripped.ApiVersion);
        Assert.AreEqual(original.Dimensions, roundTripped.Dimensions);
        Assert.AreEqual(original.TimeoutMs, roundTripped.TimeoutMs);

        // Verify endpoint
        Assert.IsNotNull(roundTripped.Endpoint);
        Assert.AreEqual(original.Endpoint!.Enabled, roundTripped.Endpoint.Enabled);
        CollectionAssert.AreEqual(original.Endpoint.Roles, roundTripped.Endpoint.Roles);

        // Verify health
        Assert.IsNotNull(roundTripped.Health);
        Assert.AreEqual(original.Health!.Enabled, roundTripped.Health.Enabled);
        Assert.AreEqual(original.Health.ThresholdMs, roundTripped.Health.ThresholdMs);
        Assert.AreEqual(original.Health.TestText, roundTripped.Health.TestText);
        Assert.AreEqual(original.Health.ExpectedDimensions, roundTripped.Health.ExpectedDimensions);

        // Verify chunking (THIS IS THE CRITICAL TEST THAT WAS MISSING)
        Assert.IsNotNull(roundTripped.Chunking, "Chunking should not be null after round-trip");
        Assert.AreEqual(original.Chunking!.Enabled, roundTripped.Chunking.Enabled);
        Assert.AreEqual(original.Chunking.SizeChars, roundTripped.Chunking.SizeChars);
        Assert.AreEqual(original.Chunking.OverlapChars, roundTripped.Chunking.OverlapChars);
    }

    /// <summary>
    /// Tests that null chunking property is handled correctly.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsOptionsWithNullChunking()
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
                    ""base-url"": ""https://test.openai.azure.com"",
                    ""api-key"": ""test-key"",
                    ""model"": ""test-model""
                }
            },
            ""entities"": {}
        }";

        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig? runtimeConfig);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        // Chunking should be null when not specified
        Assert.IsNull(embeddings.Chunking);
    }

    /// <summary>
    /// Tests serialization when chunking is null.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsOptionsSerializationWithNullChunking()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "test-key",
            Model: "test-model",
            Chunking: null);

        // Act
        JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions(replacementSettings: null);
        string json = JsonSerializer.Serialize(options, serializerOptions);

        // Assert - chunking should not be in the output when null
        Assert.IsFalse(json.Contains("\"chunking\""), $"Chunking should not appear in JSON when null: {json}");
    }

    /// <summary>
    /// Tests that endpoint and health properties are preserved during serialization.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsOptionsSerializationWithAllNestedProperties()
    {
        // Arrange
        EmbeddingsEndpointOptions endpointOptions = new(
            enabled: true,
            roles: new[] { "authenticated", "anonymous" });

        EmbeddingsHealthCheckConfig healthOptions = new(
            enabled: true,
            thresholdMs: 5000,
            testText: "test",
            expectedDimensions: 1536);

        EmbeddingsChunkingOptions chunkingOptions = new(
            enabled: false,
            sizeChars: 800,
            overlapChars: 100);

        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "sk-test",
            Endpoint: endpointOptions,
            Health: healthOptions,
            Chunking: chunkingOptions);

        // Act
        JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions(replacementSettings: null);
        string json = JsonSerializer.Serialize(options, serializerOptions);

        // Deserialize back
        EmbeddingsOptions? deserialized = JsonSerializer.Deserialize<EmbeddingsOptions>(json, serializerOptions);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.IsNotNull(deserialized.Endpoint);
        Assert.IsNotNull(deserialized.Health);
        Assert.IsNotNull(deserialized.Chunking);

        Assert.AreEqual(endpointOptions.Enabled, deserialized.Endpoint.Enabled);
        CollectionAssert.AreEqual(endpointOptions.Roles, deserialized.Endpoint.Roles);

        Assert.AreEqual(healthOptions.Enabled, deserialized.Health.Enabled);
        Assert.AreEqual(healthOptions.ThresholdMs, deserialized.Health.ThresholdMs);
        Assert.AreEqual(healthOptions.TestText, deserialized.Health.TestText);
        Assert.AreEqual(healthOptions.ExpectedDimensions, deserialized.Health.ExpectedDimensions);

        Assert.AreEqual(chunkingOptions.Enabled, deserialized.Chunking.Enabled);
        Assert.AreEqual(chunkingOptions.SizeChars, deserialized.Chunking.SizeChars);
        Assert.AreEqual(chunkingOptions.OverlapChars, deserialized.Chunking.OverlapChars);
    }

    /// <summary>
    /// Tests that disabled chunking is handled correctly.
    /// </summary>
    [TestMethod]
    public void TestEmbeddingsOptionsWithDisabledChunking()
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
                    ""base-url"": ""https://test.openai.azure.com"",
                    ""api-key"": ""test-key"",
                    ""model"": ""test-model"",
                    ""chunking"": {
                        ""enabled"": false
                    }
                }
            },
            ""entities"": {}
        }";

        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig? runtimeConfig);

        // Assert
        Assert.IsTrue(success);
        Assert.IsNotNull(runtimeConfig?.Runtime?.Embeddings);

        EmbeddingsOptions embeddings = runtimeConfig.Runtime.Embeddings;
        Assert.IsNotNull(embeddings.Chunking);
        Assert.IsFalse(embeddings.Chunking.Enabled);
        // Should use default values for size and overlap when not specified
        Assert.AreEqual(EmbeddingsChunkingOptions.DEFAULT_SIZE_CHARS, embeddings.Chunking.SizeChars);
        Assert.AreEqual(EmbeddingsChunkingOptions.DEFAULT_OVERLAP_CHARS, embeddings.Chunking.OverlapChars);
    }
}

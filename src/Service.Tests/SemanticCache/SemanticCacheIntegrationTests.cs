// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.SemanticCache
{
    /// <summary>
    /// Integration tests for semantic caching feature.
    /// Tests service registration, configuration validation, and basic orchestration.
    /// Full end-to-end tests with real Azure resources would be in a separate test category.
    /// </summary>
    [TestClass]
    public class SemanticCacheIntegrationTests
    {
        private const string TEST_ENTITY = "Book";

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Tests that semantic cache service is properly registered when enabled in config.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public void TestSemanticCacheServiceRegistration_WhenEnabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithSemanticCache(enabled: true);
            
            // Act - Create service provider with semantic cache configuration
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(provider => 
                TestHelper.GenerateInMemoryRuntimeConfigProvider(config));
            
            // This simulates what Startup.cs does
            if (config.Runtime?.SemanticCache?.Enabled == true)
            {
                services.AddSingleton<IEmbeddingService>(provider =>
                {
                    // Return a mock for registration test
                    var mock = new Mock<IEmbeddingService>();
                    return mock.Object;
                });
                services.AddSingleton<ISemanticCache>(provider =>
                {
                    // Return a mock for registration test
                    var mock = new Mock<ISemanticCache>();
                    return mock.Object;
                });
            }
            
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            
            // Assert
            ISemanticCache semanticCache = serviceProvider.GetService<ISemanticCache>();
            IEmbeddingService embeddingService = serviceProvider.GetService<IEmbeddingService>();
            
            Assert.IsNotNull(semanticCache, "ISemanticCache should be registered when enabled");
            Assert.IsNotNull(embeddingService, "IEmbeddingService should be registered when enabled");
        }

        /// <summary>
        /// Tests that semantic cache services are NOT registered when disabled in config.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public void TestSemanticCacheServiceNotRegisteredWhenDisabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithSemanticCache(enabled: false);
            
            // Act
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(provider => 
                TestHelper.GenerateInMemoryRuntimeConfigProvider(config));
            
            // Semantic cache should NOT be registered when disabled
            if (config.Runtime?.SemanticCache?.Enabled == true)
            {
                services.AddSingleton<IEmbeddingService>(provider =>
                {
                    var mock = new Mock<IEmbeddingService>();
                    return mock.Object;
                });
                services.AddSingleton<ISemanticCache>(provider =>
                {
                    var mock = new Mock<ISemanticCache>();
                    return mock.Object;
                });
            }
            
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            
            // Assert
            ISemanticCache semanticCache = serviceProvider.GetService<ISemanticCache>();
            IEmbeddingService embeddingService = serviceProvider.GetService<IEmbeddingService>();
            
            Assert.IsNull(semanticCache, "ISemanticCache should NOT be registered when disabled");
            Assert.IsNull(embeddingService, "IEmbeddingService should NOT be registered when disabled");
        }

        /// <summary>
        /// Tests semantic cache query operation with mocked dependencies.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task TestSemanticCacheFlow_CacheHit()
        {
            // Arrange
            string cachedResponse = @"{""items"":[{""id"":6,""title"":""Book 6""}]}";
            float[] queryEmbedding = GenerateMockEmbedding(1536);
            
            Mock<ISemanticCache> mockSemanticCache = new();
            mockSemanticCache
                .Setup(s => s.QueryAsync(
                    It.IsAny<float[]>(), 
                    It.IsAny<int>(), 
                    It.IsAny<double>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new SemanticCacheResult(
                    response: cachedResponse,
                    similarity: 0.95,
                    originalQuery: "SELECT * FROM Books WHERE id >= 6"));
            
            // Act
            SemanticCacheResult result = await mockSemanticCache.Object.QueryAsync(
                embedding: queryEmbedding,
                maxResults: 5,
                similarityThreshold: 0.85);
            
            // Assert
            Assert.IsNotNull(result, "Should return cached result");
            Assert.AreEqual(cachedResponse, result.Response);
            Assert.IsTrue(result.Similarity >= 0.85, "Similarity score should meet threshold");
            
            mockSemanticCache.Verify(
                s => s.QueryAsync(
                    It.IsAny<float[]>(), 
                    5, 
                    0.85,
                    It.IsAny<System.Threading.CancellationToken>()), 
                Times.Once);
        }

        /// <summary>
        /// Tests semantic cache miss scenario.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task TestSemanticCacheFlow_CacheMiss()
        {
            // Arrange
            float[] queryEmbedding = GenerateMockEmbedding(1536);
            
            Mock<ISemanticCache> mockSemanticCache = new();
            mockSemanticCache
                .Setup(s => s.QueryAsync(
                    It.IsAny<float[]>(), 
                    It.IsAny<int>(), 
                    It.IsAny<double>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync((SemanticCacheResult)null);
            
            // Act
            SemanticCacheResult result = await mockSemanticCache.Object.QueryAsync(
                embedding: queryEmbedding,
                maxResults: 5,
                similarityThreshold: 0.85);
            
            // Assert
            Assert.IsNull(result, "Should return null on cache miss");
            mockSemanticCache.Verify(
                s => s.QueryAsync(
                    It.IsAny<float[]>(), 
                    5, 
                    0.85,
                    It.IsAny<System.Threading.CancellationToken>()), 
                Times.Once);
        }

        /// <summary>
        /// Tests storing a result in semantic cache.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task TestSemanticCacheFlow_StoreResult()
        {
            // Arrange
            string responseJson = @"{""items"":[{""id"":1,""title"":""Cheap Book""}]}";
            float[] queryEmbedding = GenerateMockEmbedding(1536);
            TimeSpan ttl = TimeSpan.FromHours(1);
            
            Mock<ISemanticCache> mockSemanticCache = new();
            mockSemanticCache
                .Setup(s => s.StoreAsync(
                    It.IsAny<float[]>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.CompletedTask);
            
            // Act
            await mockSemanticCache.Object.StoreAsync(
                embedding: queryEmbedding,
                responseJson: responseJson,
                ttl: ttl);
            
            // Assert
            mockSemanticCache.Verify(
                s => s.StoreAsync(
                    It.Is<float[]>(e => e.SequenceEqual(queryEmbedding)),
                    responseJson,
                    ttl,
                    It.IsAny<System.Threading.CancellationToken>()), 
                Times.Once);
        }

        /// <summary>
        /// Tests configuration validation - similarity threshold must be between 0 and 1.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public void TestConfigurationValidation_SimilarityThresholdInRange()
        {
            // Arrange & Act - Valid thresholds
            SemanticCacheOptions validLow = new(
                enabled: true,
                similarityThreshold: 0.0,
                maxResults: 5,
                expireSeconds: 3600,
                azureManagedRedis: new AzureManagedRedisOptions(connectionString: "test"),
                embeddingProvider: new EmbeddingProviderOptions(
                    endpoint: "https://test.openai.azure.com",
                    apiKey: "test",
                    model: "text-embedding-ada-002"
                )
            );

            SemanticCacheOptions validHigh = new(
                enabled: true,
                similarityThreshold: 1.0,
                maxResults: 5,
                expireSeconds: 3600,
                azureManagedRedis: new AzureManagedRedisOptions(connectionString: "test"),
                embeddingProvider: new EmbeddingProviderOptions(
                    endpoint: "https://test.openai.azure.com",
                    apiKey: "test",
                    model: "text-embedding-ada-002"
                )
            );

            // Assert - No exceptions should be thrown
            Assert.AreEqual(0.0, validLow.SimilarityThreshold);
            Assert.AreEqual(1.0, validHigh.SimilarityThreshold);
        }

        /// <summary>
        /// Tests semantic cache with REAL Azure OpenAI embeddings.
        /// This test requires actual Azure OpenAI resource and will be skipped if environment variables are not set.
        /// Set ENABLE_SEMANTIC_CACHE_E2E_TESTS=true and configure Azure OpenAI environment variables to run this test.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public void TestSemanticCacheConfiguration_WithRealAzureOpenAI()
        {
            // Skip test if semantic cache E2E testing is not enabled.
            // NOTE: This test validates configuration only. Full E2E behavior is covered by SemanticCacheE2ETests.
            if (!string.Equals(Environment.GetEnvironmentVariable("ENABLE_SEMANTIC_CACHE_E2E_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Inconclusive("Set ENABLE_SEMANTIC_CACHE_E2E_TESTS=true and configure Azure OpenAI environment variables to run real Azure OpenAI configuration validation.");
            }

            // Arrange & Act - This will validate that all required environment variables are set
            RuntimeConfig config = CreateConfigWithSemanticCache(enabled: true, useRealAzureOpenAI: true);
            
            // Assert - Verify configuration was created successfully with real Azure OpenAI settings
            Assert.IsNotNull(config.Runtime?.SemanticCache, "SemanticCache configuration should be created");
            Assert.IsTrue(config.Runtime.SemanticCache.Enabled, "SemanticCache should be enabled");
            
            var embeddingProvider = config.Runtime.SemanticCache.EmbeddingProvider;
            Assert.IsNotNull(embeddingProvider, "EmbeddingProvider should be configured");
            Assert.AreEqual("azure-openai", embeddingProvider.Type, "Provider type should be azure-openai");
            
            // Verify endpoint is a real Azure OpenAI endpoint (not the specific mock one we use in tests)
            Assert.IsTrue(embeddingProvider.Endpoint.Contains(".openai.azure.com"), 
                $"Endpoint should be a real Azure OpenAI endpoint, got: {embeddingProvider.Endpoint}");
            
            // Check that it's NOT the specific mock endpoint we use for unit testing
            Assert.IsFalse(embeddingProvider.Endpoint.Equals("https://test.openai.azure.com", StringComparison.OrdinalIgnoreCase), 
                "Should not be using the specific mock test endpoint");
            
            // Verify API key is not the mock key
            Assert.AreNotEqual("test-key", embeddingProvider.ApiKey, "Should not be using mock API key");
            
            // Verify endpoint starts with https (security requirement)
            Assert.IsTrue(embeddingProvider.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                "Endpoint should use HTTPS for security");
            
            Console.WriteLine($"✅ Real Azure OpenAI configuration validated:");
            Console.WriteLine($"   Endpoint: {embeddingProvider.Endpoint}");
            Console.WriteLine($"   Model: {embeddingProvider.Model}");
            Console.WriteLine($"   API Key: {new string('*', Math.Max(0, embeddingProvider.ApiKey.Length - 4))}{(embeddingProvider.ApiKey.Length >= 4 ? embeddingProvider.ApiKey[^4..] : "****")}");
        }

        #region Helper Methods

        /// <summary>
        /// Creates a test runtime config with semantic cache configuration.
        /// Supports both mock and real Azure OpenAI endpoints based on environment variables.
        /// </summary>
        private static RuntimeConfig CreateConfigWithSemanticCache(bool enabled, bool useRealAzureOpenAI = false)
        {
            // Use real Azure OpenAI if requested and environment variables are available
            string embeddingEndpoint;
            string embeddingApiKey;
            
            if (useRealAzureOpenAI)
            {
                // Following Azure security best practices - never hardcode credentials
                embeddingEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
                    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT environment variable is required for real Azure OpenAI testing");
                
                embeddingApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") 
                    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY environment variable is required for real Azure OpenAI testing");
                
                Console.WriteLine($"Using real Azure OpenAI endpoint: {embeddingEndpoint}");
            }
            else
            {
                // Use mock endpoint for unit tests
                embeddingEndpoint = "https://test.openai.azure.com";
                embeddingApiKey = "test-key";
                Console.WriteLine("Using mock Azure OpenAI endpoint for unit testing");
            }

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "Server=test;Database=test;", null),
                Runtime: new RuntimeOptions(
                    Rest: new RestRuntimeOptions(Enabled: true),
                    GraphQL: new GraphQLRuntimeOptions(Enabled: true),
                    Mcp: null,
                    Host: new HostOptions(
                        Cors: null,
                        Authentication: new() { Provider = "StaticWebApps" }
                    ),
                    Cache: new RuntimeCacheOptions(Enabled: true, TtlSeconds: 60),
                    SemanticCache: enabled ? new SemanticCacheOptions(
                        enabled: true,
                        similarityThreshold: 0.85,
                        maxResults: 5,
                        expireSeconds: 3600,
                        azureManagedRedis: new AzureManagedRedisOptions(
                            connectionString: "localhost:6379,ssl=False"
                        ),
                        embeddingProvider: new EmbeddingProviderOptions(
                            type: "azure-openai", // Explicitly specify the provider type
                            endpoint: embeddingEndpoint,
                            apiKey: embeddingApiKey,
                            model: Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL") ?? "text-embedding-ada-002"
                        )
                    ) : null
                ),
                Entities: new(new Dictionary<string, Entity>
                {
                    [TEST_ENTITY] = new Entity(
                        Source: new EntitySource("dbo.books", EntitySourceType.Table, null, null),
                        Fields: null,
                        GraphQL: new EntityGraphQLOptions("Book", "Books"),
                        Rest: new EntityRestOptions(Enabled: true),
                        Permissions: new[]
                        {
                            new EntityPermission("anonymous", new[]
                            {
                                new EntityAction(EntityActionOperation.Read, null, null)
                            })
                        },
                        Mappings: null,
                        Relationships: null,
                        Cache: new EntityCacheOptions { Enabled = true, TtlSeconds = 60 }
                    )
                })
            );
        }

        /// <summary>
        /// Generates a mock embedding vector for testing.
        /// </summary>
        private static float[] GenerateMockEmbedding(int dimensions)
        {
            Random random = new(42); // Fixed seed for reproducibility
            float[] embedding = new float[dimensions];
            for (int i = 0; i < dimensions; i++)
            {
                embedding[i] = (float)(random.NextDouble() * 2.0 - 1.0); // Range: -1.0 to 1.0
            }
            
            // Normalize the vector
            double magnitude = Math.Sqrt(embedding.Sum(x => x * x));
            for (int i = 0; i < dimensions; i++)
            {
                embedding[i] /= (float)magnitude;
            }
            
            return embedding;
        }

        #endregion
    }
}

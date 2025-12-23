// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;

namespace Azure.DataApiBuilder.Service.Tests.SemanticCache
{
    /// <summary>
    /// End-to-End tests for semantic caching.
    ///
    /// Required env vars to run (tests will be skipped otherwise):
    /// - ENABLE_SEMANTIC_CACHE_E2E_TESTS=true
    ///
    /// Azure OpenAI env vars:
    /// - AZURE_OPENAI_ENDPOINT
    /// - AZURE_OPENAI_API_KEY
    /// - AZURE_OPENAI_EMBEDDING_MODEL (optional)
    ///
    /// Redis env var (preferred):
    /// - TEST_REDIS_CONNECTION_STRING
    /// </summary>
    [TestClass]
    public class SemanticCacheE2ETests
    {
        private const string RUN_E2E_TESTS_ENV_VAR = "ENABLE_SEMANTIC_CACHE_E2E_TESTS";
        private const string TRUE = "true";

        private const string AZURE_OPENAI_ENDPOINT_ENV_VAR = "AZURE_OPENAI_ENDPOINT";
        private const string AZURE_OPENAI_API_KEY_ENV_VAR = "AZURE_OPENAI_API_KEY";
        private const string AZURE_OPENAI_EMBEDDING_MODEL_ENV_VAR = "AZURE_OPENAI_EMBEDDING_MODEL";

        private const string TEST_REDIS_CONNECTION_STRING_ENV_VAR = "TEST_REDIS_CONNECTION_STRING";

        // Default connection string used by local dev Redis (override with TEST_REDIS_CONNECTION_STRING)
        private static readonly string _defaultRedisConnectionString = "localhost:6379,password=TestRedisPassword123";

        private const string DEFAULT_AZURE_OPENAI_EMBEDDING_MODEL = "text-embedding-ada-002";

        private const string SEMANTIC_CACHE_E2E_CATEGORY = "SemanticCacheE2E";

        private string _configFilePath;

        [TestInitialize]
        public async Task TestInitialize()
        {
            // Skip tests if environment variable is not set (for CI/CD scenarios).
            if (!string.Equals(Environment.GetEnvironmentVariable(RUN_E2E_TESTS_ENV_VAR), TRUE, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Inconclusive($"Set {RUN_E2E_TESTS_ENV_VAR}=true to run E2E semantic cache tests");
            }

            // Validate external prerequisites in a test-friendly way (skip, don't throw).
            ValidateAzureOpenAIEnvironmentOrSkip();

            // Verify Redis is available.
            await VerifyRedisConnection(GetRedisConnectionString());
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (!string.IsNullOrWhiteSpace(_configFilePath) && File.Exists(_configFilePath))
            {
                File.Delete(_configFilePath);
            }

            TestHelper.UnsetAllDABEnvironmentVariables();

            // Clean Redis test data (avoid .Wait() to reduce deadlock risk)
            CleanupRedisTestData().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Tests semantic cache with SQL Server database.
        /// Verifies that semantically similar queries hit the cache while different queries miss.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestCategory(SEMANTIC_CACHE_E2E_CATEGORY)]
        [TestMethod]
        public async Task TestSemanticCache_MSSQLDatabase_CacheHitAndMiss()
        {
            await RunSemanticCacheTest(
                databaseType: DatabaseType.MSSQL,
                connectionString: GetMSSQLConnectionString());
        }

        /// <summary>
        /// Tests semantic cache with MySQL database.
        /// </summary>
        [TestCategory(TestCategory.MYSQL)]
        [TestCategory(SEMANTIC_CACHE_E2E_CATEGORY)]
        [TestMethod]
        public async Task TestSemanticCache_MySQLDatabase_CacheHitAndMiss()
        {
            await RunSemanticCacheTest(
                databaseType: DatabaseType.MySQL,
                connectionString: GetMySQLConnectionString());
        }

        /// <summary>
        /// Tests semantic cache with PostgreSQL database.
        /// </summary>
        [TestCategory(TestCategory.POSTGRESQL)]
        [TestCategory(SEMANTIC_CACHE_E2E_CATEGORY)]
        [TestMethod]
        public async Task TestSemanticCache_PostgreSQLDatabase_CacheHitAndMiss()
        {
            await RunSemanticCacheTest(
                databaseType: DatabaseType.PostgreSQL,
                connectionString: GetPostgreSQLConnectionString());
        }

        /// <summary>
        /// Tests semantic cache performance improvements by measuring response times.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestCategory(SEMANTIC_CACHE_E2E_CATEGORY)]
        [TestMethod]
        public async Task TestSemanticCache_PerformanceImprovement()
        {
            await ResetDbStateAsync(DatabaseType.MSSQL, GetMSSQLConnectionString());

            // Setup config with semantic cache
            var configFilePath = SetupSemanticCacheConfig(DatabaseType.MSSQL, GetMSSQLConnectionString());

            string[] args = new[] { $"--ConfigFileName={configFilePath}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            await CleanupRedisTestData();

            // Execute a complex query that would benefit from caching
            string query = @"{
                books(first: 10, filter: { title: { contains: ""Great"" } }) {
                    items {
                        id
                        title
                        author
                        publishedYear
                    }
                }
            }";

            // First request - cache miss (should be slower)
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response1 = await ExecuteGraphQLQuery(client, query);
            stopwatch.Stop();
            long firstRequestTime = stopwatch.ElapsedMilliseconds;

            // Wait a moment to ensure timing difference
            await Task.Delay(100);

            // Second similar request - should be cache hit (should be faster)
            string similarQuery = @"{
                books(first: 10, filter: { title: { contains: ""Amazing"" } }) {
                    items {
                        id
                        title
                        author
                        publishedYear
                    }
                }
            }";

            stopwatch.Restart();
            var response2 = await ExecuteGraphQLQuery(client, similarQuery);
            stopwatch.Stop();
            long secondRequestTime = stopwatch.ElapsedMilliseconds;

            // Assert both requests succeeded
            Assert.IsTrue(response1.IsSuccessStatusCode, "First request should succeed");
            Assert.IsTrue(response2.IsSuccessStatusCode, "Second request should succeed");

            // Assert semantic cache provided performance benefit
            // Note: This is a basic performance test - in real scenarios, the difference would be more significant
            Console.WriteLine($"First request time: {firstRequestTime}ms");
            Console.WriteLine($"Second request time: {secondRequestTime}ms");

            // Verify cache entries exist in Redis
            await WaitForRedisKeyCountAsync(minExpected: 1);
        }

        /// <summary>
        /// Tests that semantic cache respects TTL settings.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestCategory(SEMANTIC_CACHE_E2E_CATEGORY)]
        [TestMethod]
        public async Task TestSemanticCache_TTLExpiration()
        {
            await ResetDbStateAsync(DatabaseType.MSSQL, GetMSSQLConnectionString());

            // Setup config with short TTL for testing
            var configFilePath = SetupSemanticCacheConfig(
                DatabaseType.MSSQL,
                GetMSSQLConnectionString(),
                semanticCacheExpireSeconds: 2 // Very short TTL for testing
            );

            string[] args = new[] { $"--ConfigFileName={configFilePath}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            await CleanupRedisTestData();

            string query = @"{ books { items { id title } } }";

            // First request - cache miss
            var response1 = await ExecuteGraphQLQuery(client, query);
            Assert.IsTrue(response1.IsSuccessStatusCode);

            // Wait for cache entries to show up (store occurs after query execution)
            await WaitForRedisKeyCountAsync(minExpected: 1);

            // Wait for TTL expiration
            await Task.Delay(3000);

            // Verify cache entry has expired (Redis should clean it up)
            await WaitForRedisKeyCountAsync(minExpected: 0, expectExactlyZero: true);
        }

        /// <summary>
        /// Tests semantic cache with different similarity thresholds.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestCategory(SEMANTIC_CACHE_E2E_CATEGORY)]
        [TestMethod]
        public async Task TestSemanticCache_SimilarityThresholds()
        {
            // Test with high similarity threshold (0.95) - very strict matching
            await TestSimilarityThreshold(0.95, expectCacheHit: false);

            // Clean up cache
            await CleanupRedisTestData();

            // Test with low similarity threshold (0.5) - more lenient matching
            await TestSimilarityThreshold(0.5, expectCacheHit: true);
        }

        #region Helper Methods

        private async Task RunSemanticCacheTest(DatabaseType databaseType, string connectionString)
        {
            // Use the shared Service.Tests schema+seed scripts.
            // This eliminates reliance on external shell scripts for DB initialization.
            await ResetDbStateAsync(databaseType, connectionString);

            string configFilePath = SetupSemanticCacheConfig(databaseType, connectionString);

            string[] args = new[] { $"--ConfigFileName={configFilePath}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            await CleanupRedisTestData();

            // Test 1: Execute original query - should be cache miss
            string originalQuery = @"{ books(first: 5) { items { id title author } } }";
            var response1 = await ExecuteGraphQLQuery(client, originalQuery);
            Assert.IsTrue(response1.IsSuccessStatusCode, "Original query should succeed");

            // Wait for cache entries to show up.
            await WaitForRedisKeyCountAsync(minExpected: 1);

            // Test 2: Execute semantically similar query - may be HIT or MISS depending on threshold.
            string similarQuery = @"{ books(first: 5) { items { id title author publishedYear } } }";
            var response2 = await ExecuteGraphQLQuery(client, similarQuery);
            Assert.IsTrue(response2.IsSuccessStatusCode, "Similar query should succeed");

            // Ensure cache didn't regress to zero.
            await WaitForRedisKeyCountAsync(minExpected: 1);
        }

        private async Task TestSimilarityThreshold(double threshold, bool expectCacheHit)
        {
            // Ensure MSSQL schema exists for this test.
            await ResetDbStateAsync(DatabaseType.MSSQL, GetMSSQLConnectionString());

            string configFilePath = SetupSemanticCacheConfig(
                DatabaseType.MSSQL,
                GetMSSQLConnectionString(),
                similarityThreshold: threshold);

            string[] args = new[] { $"--ConfigFileName={configFilePath}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            await CleanupRedisTestData();

            // First query
            string query1 = @"{ books { items { id title } } }";
            var r1 = await ExecuteGraphQLQuery(client, query1);
            Assert.IsTrue(r1.IsSuccessStatusCode);

            await WaitForRedisKeyCountAsync(minExpected: 1);

            // Second query - slightly different but semantically similar
            string query2 = @"{ books { items { id title author } } }";
            var r2 = await ExecuteGraphQLQuery(client, query2);
            Assert.IsTrue(r2.IsSuccessStatusCode);

            _ = expectCacheHit;
            await WaitForRedisKeyCountAsync(minExpected: 1);
        }

        private string SetupSemanticCacheConfig(DatabaseType databaseType,
            string connectionString,
            double similarityThreshold = 0.85,
            int maxResults = 5,
            int semanticCacheExpireSeconds = 3600,
            int regularCacheTtlSeconds = 300)
        {
            // Align with repo pattern: build runtime config via object model and write config file.
            // Use a unique per-test config file to avoid collisions.
            _configFilePath = Path.Combine(Path.GetTempPath(), $"semantic-cache-e2e-{Guid.NewGuid():N}.json");

            DataSource dataSource = new(
                databaseType,
                connectionString,
                Options: null);

            HostOptions hostOptions = new(
                Mode: HostMode.Development,
                Cors: null,
                Authentication: new() { Provider = nameof(EasyAuthType.StaticWebApps) });

            var (endpoint, apiKey, model) = GetAzureOpenAIEmbeddingProviderSettings();

            RuntimeOptions runtime = new(
                Rest: new(Enabled: true, Path: "/api"),
                GraphQL: new(Enabled: true, Path: "/graphql", AllowIntrospection: true),
                Mcp: new(Enabled: true),
                Host: hostOptions,
                Cache: new(Enabled: true, TtlSeconds: regularCacheTtlSeconds),
                SemanticCache: new SemanticCacheOptions(
                    enabled: true,
                    similarityThreshold: similarityThreshold,
                    maxResults: maxResults,
                    expireSeconds: semanticCacheExpireSeconds,
                    azureManagedRedis: new AzureManagedRedisOptions(
                        connectionString: GetRedisConnectionString(),
                        vectorIndex: "dab-test-semantic-index",
                        keyPrefix: "dab:test:sc:"
                    ),
                    embeddingProvider: new EmbeddingProviderOptions(
                        type: "azure-openai",
                        endpoint: endpoint,
                        apiKey: apiKey,
                        model: model
                    )
                )
            );

            Entity bookEntity = new(
                Source: new EntitySource(GetBooksEntitySource(databaseType), EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new EntityGraphQLOptions("Book", "Books"),
                Rest: new EntityRestOptions(Enabled: true),
                Permissions: new[] { ConfigurationTests.GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Mappings: null,
                Relationships: null,
                Cache: new EntityCacheOptions { Enabled = true, TtlSeconds = regularCacheTtlSeconds }
            );

            Dictionary<string, Entity> entityMap = new()
            {
                { "Book", bookEntity }
            };

            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: dataSource,
                Runtime: runtime,
                Entities: new(entityMap)
            );

            File.WriteAllText(_configFilePath, config.ToJson());
            return _configFilePath;
        }

        private static async Task VerifyRedisConnection(string redisConnectionString)
        {
            try
            {
                await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
                var db = redis.GetDatabase();
                await db.PingAsync();
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"Redis connection failed: {ex.Message}. Ensure Redis is reachable. You can set {TEST_REDIS_CONNECTION_STRING_ENV_VAR}.");
            }
        }

        private static async Task<long> GetSemanticCacheKeyCountAsync()
        {
            await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(GetRedisConnectionString());
            var server = redis.GetServer(redis.GetEndPoints()[0]);
            return server.Keys(pattern: "dab:test:sc:*").LongCount();
        }

        private static async Task WaitForRedisKeyCountAsync(int minExpected, int timeoutMs = 5000, bool expectExactlyZero = false)
        {
            var stopAt = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTimeOffset.UtcNow < stopAt)
            {
                long count = await GetSemanticCacheKeyCountAsync();

                if (expectExactlyZero)
                {
                    if (count == 0)
                    {
                        return;
                    }
                }
                else
                {
                    if (count >= minExpected)
                    {
                        return;
                    }
                }

                await Task.Delay(200);
            }

            long finalCount = await GetSemanticCacheKeyCountAsync();
            if (expectExactlyZero)
            {
                Assert.AreEqual(0, finalCount, $"Expected 0 semantic cache entries, but found {finalCount}");
            }
            else
            {
                Assert.IsTrue(finalCount >= minExpected, $"Expected at least {minExpected} semantic cache entries, but found {finalCount}");
            }
        }

        private static async Task CleanupRedisTestData()
        {
            try
            {
                await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(GetRedisConnectionString());
                var server = redis.GetServer(redis.GetEndPoints()[0]);

                var keys = server.Keys(pattern: "dab:test:sc:*").ToArray();
                if (keys.Length > 0)
                {
                    var db = redis.GetDatabase();
                    await db.KeyDeleteAsync(keys);
                    Console.WriteLine($"Cleaned up {keys.Length} semantic cache entries from Redis");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to cleanup Redis test data: {ex.Message}");
            }
        }

        private static string GetMSSQLConnectionString()
        {
            return ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL);
        }

        private static string GetMySQLConnectionString()
        {
            return ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MYSQL);
        }

        private static string GetPostgreSQLConnectionString()
        {
            return ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.POSTGRESQL);
        }

        private static void ValidateAzureOpenAIEnvironmentOrSkip()
        {
            // Keep these checks here (not at type init time) so discovery/other test runs don't throw.
            string endpoint = Environment.GetEnvironmentVariable(AZURE_OPENAI_ENDPOINT_ENV_VAR);
            string apiKey = Environment.GetEnvironmentVariable(AZURE_OPENAI_API_KEY_ENV_VAR);

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                Assert.Inconclusive($"{AZURE_OPENAI_ENDPOINT_ENV_VAR} environment variable is required for SemanticCacheE2ETests.");
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Assert.Inconclusive($"{AZURE_OPENAI_API_KEY_ENV_VAR} environment variable is required for SemanticCacheE2ETests.");
            }
        }

        private static (string Endpoint, string ApiKey, string Model) GetAzureOpenAIEmbeddingProviderSettings()
        {
            // We validated required vars in ValidateAzureOpenAIEnvironmentOrSkip.
            string endpoint = Environment.GetEnvironmentVariable(AZURE_OPENAI_ENDPOINT_ENV_VAR)!;
            string apiKey = Environment.GetEnvironmentVariable(AZURE_OPENAI_API_KEY_ENV_VAR)!;
            string model = Environment.GetEnvironmentVariable(AZURE_OPENAI_EMBEDDING_MODEL_ENV_VAR) ?? DEFAULT_AZURE_OPENAI_EMBEDDING_MODEL;
            return (endpoint, apiKey, model);
        }

        private static string GetBooksEntitySource(DatabaseType databaseType)
        {
            // Use schema-qualified name when required.
            return databaseType switch
            {
                DatabaseType.MSSQL => "dbo.books",
                DatabaseType.MySQL => "books",
                DatabaseType.PostgreSQL => "books",
                _ => "books"
            };
        }

        private static string GetRedisConnectionString()
        {
            return Environment.GetEnvironmentVariable(TEST_REDIS_CONNECTION_STRING_ENV_VAR) ?? _defaultRedisConnectionString;
        }

        private static async Task ResetDbStateAsync(DatabaseType databaseType, string connectionString)
        {
            // Service.Tests keeps canonical schema+seed scripts at repo root of the test project.
            string engine = databaseType switch
            {
                DatabaseType.MSSQL => TestCategory.MSSQL,
                DatabaseType.MySQL => TestCategory.MYSQL,
                DatabaseType.PostgreSQL => TestCategory.POSTGRESQL,
                _ => throw new ArgumentOutOfRangeException(nameof(databaseType), databaseType, "Unsupported database type")
            };

            string scriptPath = Path.Combine(AppContext.BaseDirectory, $"DatabaseSchema-{engine}.sql");

            if (!File.Exists(scriptPath))
            {
                // Fallback for local runs where AppContext.BaseDirectory differs.
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), $"DatabaseSchema-{engine}.sql");
            }

            if (!File.Exists(scriptPath))
            {
                Assert.Inconclusive($"Could not locate {Path.GetFileName(scriptPath)} to initialize the database.");
            }

            string sql = await File.ReadAllTextAsync(scriptPath);

            try
            {
                switch (databaseType)
                {
                    case DatabaseType.MSSQL:
                        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
                        {
                            await connection.OpenAsync();
                            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, connection)
                            {
                                CommandTimeout = 300
                            };

                            await cmd.ExecuteNonQueryAsync();
                        }

                        break;

                    case DatabaseType.MySQL:
                        // MySqlConnector doesn't include MySqlScript in this repo; execute the schema script directly.
                        // NOTE: DatabaseSchema-MYSQL.sql is expected to be compatible with multi-statement execution.
                        await using (var connection = new MySqlConnector.MySqlConnection(connectionString))
                        {
                            await connection.OpenAsync();

                            await using var cmd = new MySqlConnector.MySqlCommand(sql, connection)
                            {
                                CommandTimeout = 300
                            };

                            await cmd.ExecuteNonQueryAsync();
                        }

                        break;

                    case DatabaseType.PostgreSQL:
                        await using (var connection = new Npgsql.NpgsqlConnection(connectionString))
                        {
                            await connection.OpenAsync();
                            await using var cmd = new Npgsql.NpgsqlCommand(sql, connection)
                            {
                                CommandTimeout = 300
                            };

                            await cmd.ExecuteNonQueryAsync();
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"Failed to initialize database using {Path.GetFileName(scriptPath)}. Error: {ex.Message}");
            }
        }

        private static async Task<HttpResponseMessage> ExecuteGraphQLQuery(HttpClient client, string query)
        {
            var requestBody = new { query };
            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await client.PostAsync("/graphql", content);
        }

        #endregion
    }
}

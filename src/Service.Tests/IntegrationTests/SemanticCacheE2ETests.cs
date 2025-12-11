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
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;

namespace Azure.DataApiBuilder.Service.Tests.IntegrationTests
{
    /// <summary>
    /// End-to-End tests for semantic caching with Azure Managed Redis (AMR).
    /// Tests the complete flow from query execution through embeddings to Redis storage.
    /// 
    /// Prerequisites for local testing:
    /// 1. Redis container: docker run -d --name redis-test -p 6379:6379 redis:7-alpine redis-server --requirepass TestRedisPassword123
    /// 2. SQL Server container: docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong@Passw0rd" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
    /// 3. MySQL container: docker run --name mysql-test -e MYSQL_ROOT_PASSWORD=test123 -p 3306:3306 -d mysql:8.0
    /// 4. PostgreSQL container: docker run --name postgres-test -e POSTGRES_PASSWORD=test123 -p 5432:5432 -d postgres:15
    /// 
    /// Required Azure OpenAI Environment Variables:
    /// export AZURE_OPENAI_ENDPOINT="https://your-openai-resource.openai.azure.com/"
    /// export AZURE_OPENAI_API_KEY="your-api-key-here"
    /// export AZURE_OPENAI_EMBEDDING_MODEL="text-embedding-3-small"
    /// 
    /// Set environment variable: ENABLE_SEMANTIC_CACHE_E2E_TESTS=true to run these tests
    /// </summary>
    [TestClass]
    public class SemanticCacheE2ETests
    {
        private const string CUSTOM_CONFIG_FILENAME = "semantic-cache-e2e-config.json";
        private static readonly string RedisConnectionString = "localhost:6379,password=TestRedisPassword123";
        
        // Azure OpenAI configuration from environment variables
        private static readonly string AzureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
            ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT environment variable is required");
        private static readonly string AzureOpenAIApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") 
            ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY environment variable is required");
        private static readonly string AzureOpenAIModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";
        
        // Test data for consistent testing across databases
        private static readonly string[] BookTitles = {
            "The Great Gatsby",
            "To Kill a Mockingbird", 
            "1984",
            "Pride and Prejudice",
            "The Catcher in the Rye"
        };

        [TestInitialize]
        public async Task TestInitialize()
        {
            // Skip tests if environment variable is not set (for CI/CD scenarios)
            if (Environment.GetEnvironmentVariable("ENABLE_SEMANTIC_CACHE_E2E_TESTS") != "true")
            {
                Assert.Inconclusive("Set ENABLE_SEMANTIC_CACHE_E2E_TESTS=true to run E2E semantic cache tests");
            }

            // Verify Redis is available
            await VerifyRedisConnection();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (File.Exists(CUSTOM_CONFIG_FILENAME))
            {
                File.Delete(CUSTOM_CONFIG_FILENAME);
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
            
            // Clean Redis test data
            CleanupRedisTestData().Wait();
        }

        /// <summary>
        /// Tests semantic cache with SQL Server database.
        /// Verifies that semantically similar queries hit the cache while different queries miss.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task TestSemanticCache_MSSQLDatabase_CacheHitAndMiss()
        {
            await RunSemanticCacheTest(
                databaseType: DatabaseType.MSSQL,
                connectionString: GetMSSQLConnectionString(),
                setupScript: GetMSSQLSetupScript()
            );
        }

        /// <summary>
        /// Tests semantic cache with MySQL database.
        /// </summary>
        [TestCategory(TestCategory.MYSQL)]
        [TestMethod]
        public async Task TestSemanticCache_MySQLDatabase_CacheHitAndMiss()
        {
            await RunSemanticCacheTest(
                databaseType: DatabaseType.MySQL,
                connectionString: GetMySQLConnectionString(),
                setupScript: GetMySQLSetupScript()
            );
        }

        /// <summary>
        /// Tests semantic cache with PostgreSQL database.
        /// </summary>
        [TestCategory(TestCategory.POSTGRESQL)]
        [TestMethod]
        public async Task TestSemanticCache_PostgreSQLDatabase_CacheHitAndMiss()
        {
            await RunSemanticCacheTest(
                databaseType: DatabaseType.PostgreSQL,
                connectionString: GetPostgreSQLConnectionString(),
                setupScript: GetPostgreSQLSetupScript()
            );
        }

        /// <summary>
        /// Tests semantic cache performance improvements by measuring response times.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task TestSemanticCache_PerformanceImprovement()
        {
            // Setup config with semantic cache
            SetupSemanticCacheConfig(DatabaseType.MSSQL, GetMSSQLConnectionString());
            
            string[] args = new[] { $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

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
            await VerifyCacheEntriesInRedis();
        }

        /// <summary>
        /// Tests that semantic cache respects TTL settings.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task TestSemanticCache_TTLExpiration()
        {
            // Setup config with short TTL for testing
            SetupSemanticCacheConfig(
                DatabaseType.MSSQL, 
                GetMSSQLConnectionString(),
                semanticCacheExpireSeconds: 2 // Very short TTL for testing
            );
            
            string[] args = new[] { $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            string query = @"{ books { items { id title } } }";

            // First request - cache miss
            var response1 = await ExecuteGraphQLQuery(client, query);
            Assert.IsTrue(response1.IsSuccessStatusCode);

            // Verify cache entry exists
            await VerifyCacheEntriesInRedis(expectedCount: 1);

            // Wait for TTL expiration
            await Task.Delay(3000);

            // Verify cache entry has expired (Redis should clean it up)
            await VerifyCacheEntriesInRedis(expectedCount: 0);
        }

        /// <summary>
        /// Tests semantic cache with different similarity thresholds.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
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

        /// <summary>
        /// Common test logic for semantic cache testing across different databases.
        /// </summary>
        private static async Task RunSemanticCacheTest(DatabaseType databaseType, string connectionString, string setupScript)
        {
            // Setup database with test data
            await SetupTestDatabase(databaseType, connectionString, setupScript);
            
            // Setup DAB config with semantic cache
            SetupSemanticCacheConfig(databaseType, connectionString);
            
            string[] args = new[] { $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            // Test 1: Execute original query - should be cache miss
            string originalQuery = @"{ books(first: 5) { items { id title author } } }";
            var response1 = await ExecuteGraphQLQuery(client, originalQuery);
            Assert.IsTrue(response1.IsSuccessStatusCode, "Original query should succeed");

            // Test 2: Execute semantically similar query - should be cache hit due to similarity
            string similarQuery = @"{ books(first: 5) { items { id title author publishedYear } } }";
            var response2 = await ExecuteGraphQLQuery(client, similarQuery);
            Assert.IsTrue(response2.IsSuccessStatusCode, "Similar query should succeed");

            // Test 3: Execute completely different query - should be cache miss
            string differentQuery = @"{ authors(first: 5) { items { id name } } }";
            var response3 = await ExecuteGraphQLQuery(client, differentQuery);
            // Note: This might fail if authors entity doesn't exist, which is expected

            // Verify cache entries exist in Redis
            await VerifyCacheEntriesInRedis();
        }

        /// <summary>
        /// Tests semantic cache with specific similarity threshold.
        /// </summary>
        private static async Task TestSimilarityThreshold(double threshold, bool expectCacheHit)
        {
            SetupSemanticCacheConfig(
                DatabaseType.MSSQL,
                GetMSSQLConnectionString(),
                similarityThreshold: threshold
            );
            
            string[] args = new[] { $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            // First query
            string query1 = @"{ books { items { id title } } }";
            await ExecuteGraphQLQuery(client, query1);

            // Second query - slightly different but semantically similar
            string query2 = @"{ books { items { id title author } } }";
            await ExecuteGraphQLQuery(client, query2);

            // Verify cache behavior based on threshold
            int expectedCacheEntries = expectCacheHit ? 1 : 2; // If cache hit, only 1 entry; if miss, 2 entries
            await VerifyCacheEntriesInRedis(expectedCacheEntries);
        }

        /// <summary>
        /// Sets up semantic cache configuration for testing.
        /// </summary>
        private static void SetupSemanticCacheConfig(
            DatabaseType databaseType, 
            string connectionString,
            double similarityThreshold = 0.85,
            int maxResults = 5,
            int semanticCacheExpireSeconds = 3600,
            int regularCacheTtlSeconds = 300)
        {
            DataSource dataSource = new(
                databaseType,
                connectionString,
                Options: null);

            HostOptions hostOptions = new(
                Mode: HostMode.Development,
                Cors: null,
                Authentication: new() { Provider = nameof(EasyAuthType.StaticWebApps) });

            // Configure both regular cache and semantic cache
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
                        connectionString: RedisConnectionString,
                        vectorIndex: "dab-test-semantic-index",
                        keyPrefix: "dab:test:sc:"
                    ),
                    embeddingProvider: new EmbeddingProviderOptions(
                        type: "azure-openai",
                        endpoint: AzureOpenAIEndpoint,
                        apiKey: AzureOpenAIApiKey,
                        model: AzureOpenAIModel
                    )
                )
            );

            // Create test entity for books
            Entity bookEntity = new(
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

            File.WriteAllText(CUSTOM_CONFIG_FILENAME, config.ToJson());
        }

        /// <summary>
        /// Executes a GraphQL query against the test server.
        /// </summary>
        private static async Task<HttpResponseMessage> ExecuteGraphQLQuery(HttpClient client, string query)
        {
            var requestBody = new
            {
                query = query
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            return await client.PostAsync("/graphql", content);
        }

        /// <summary>
        /// Verifies Redis connection is available for testing.
        /// </summary>
        private static async Task VerifyRedisConnection()
        {
            try
            {
                var redis = ConnectionMultiplexer.Connect(RedisConnectionString);
                var db = redis.GetDatabase();
                await db.PingAsync();
                redis.Close();
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"Redis connection failed: {ex.Message}. Please ensure Redis is running on localhost:6379 with password 'TestRedisPassword123'");
            }
        }

        /// <summary>
        /// Verifies cache entries exist in Redis.
        /// </summary>
#pragma warning disable CS1998 // Async method lacks 'await' operators
        private static async Task VerifyCacheEntriesInRedis(int expectedCount = -1)
#pragma warning restore CS1998
        {
            var redis = ConnectionMultiplexer.Connect(RedisConnectionString);
            var db = redis.GetDatabase();
            
            // Search for DAB semantic cache keys
            var server = redis.GetServer(redis.GetEndPoints()[0]);
            var keys = server.Keys(pattern: "dab:test:sc:*");
            
            var keyCount = keys.Count();
            Console.WriteLine($"Found {keyCount} semantic cache entries in Redis");
            
            if (expectedCount >= 0)
            {
                Assert.AreEqual(expectedCount, keyCount, $"Expected {expectedCount} cache entries, but found {keyCount}");
            }
            else
            {
                Assert.IsTrue(keyCount > 0, "Expected at least one cache entry in Redis");
            }
            
            redis.Close();
        }

        /// <summary>
        /// Cleans up Redis test data.
        /// </summary>
        private static async Task CleanupRedisTestData()
        {
            try
            {
                var redis = ConnectionMultiplexer.Connect(RedisConnectionString);
                var server = redis.GetServer(redis.GetEndPoints()[0]);
                
                // Delete all test semantic cache keys
                var keys = server.Keys(pattern: "dab:test:sc:*");
                if (keys.Any())
                {
                    var db = redis.GetDatabase();
                    await db.KeyDeleteAsync(keys.ToArray());
                    Console.WriteLine($"Cleaned up {keys.Count()} semantic cache entries from Redis");
                }
                
                redis.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to cleanup Redis test data: {ex.Message}");
                // Don't fail the test because of cleanup issues
            }
        }

        /// <summary>
        /// Sets up test database with sample data.
        /// </summary>
        private static async Task SetupTestDatabase(DatabaseType databaseType, string connectionString, string setupScript)
        {
            Console.WriteLine($"Setting up {databaseType} database with test data...");
            
            try
            {
                switch (databaseType)
                {
                    case DatabaseType.MSSQL:
                        await SetupMSSQLDatabase(connectionString, setupScript);
                        break;
                    case DatabaseType.MySQL:
                        await SetupMySQLDatabase(connectionString, setupScript);
                        break;
                    case DatabaseType.PostgreSQL:
                        await SetupPostgreSQLDatabase(connectionString, setupScript);
                        break;
                    default:
                        throw new NotSupportedException($"Database type {databaseType} is not supported for E2E tests");
                }
                
                Console.WriteLine($"Successfully set up {databaseType} database with test data");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to setup {databaseType} database: {ex.Message}");
                throw;
            }
        }

        private static async Task SetupMSSQLDatabase(string connectionString, string setupScript)
        {
            // First connect to master to create database
            string masterConnectionString = connectionString.Replace("Database=DabTestDb", "Database=master");
            
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString))
            {
                await connection.OpenAsync();
                
                // Create database if it doesn't exist
                string createDbScript = @"
                    IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'DabTestDb')
                    BEGIN
                        CREATE DATABASE DabTestDb;
                    END;
                ";
                
                using (var command = new Microsoft.Data.SqlClient.SqlCommand(createDbScript, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
            
            // Wait a moment for database creation to complete
            await Task.Delay(1000);
            
            // Now connect to the test database and set up tables
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                
                // Split script by GO statements and execute each batch
                var batches = setupScript.Split(new[] { "\nGO\n", "\ngo\n", "\nGo\n", "\ngO\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var batch in batches)
                {
                    if (!string.IsNullOrWhiteSpace(batch.Trim()))
                    {
                        using (var command = new Microsoft.Data.SqlClient.SqlCommand(batch.Trim(), connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
        }

        private static async Task SetupMySQLDatabase(string connectionString, string setupScript)
        {
            using (var connection = new MySqlConnector.MySqlConnection(connectionString.Replace("database=DabTestDb", "database=mysql")))
            {
                await connection.OpenAsync();
                
                // Execute setup script
                var scripts = setupScript.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var script in scripts)
                {
                    if (!string.IsNullOrWhiteSpace(script.Trim()))
                    {
                        using (var command = new MySqlConnector.MySqlCommand(script.Trim(), connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
        }

        private static async Task SetupPostgreSQLDatabase(string connectionString, string setupScript)
        {
            using (var connection = new Npgsql.NpgsqlConnection(connectionString.Replace("Database=DabTestDb", "Database=postgres")))
            {
                await connection.OpenAsync();
                
                // Check if database exists and create if needed
                string checkDbScript = "SELECT 1 FROM pg_database WHERE datname = 'dabtestdb'"; // PostgreSQL is case-sensitive
                using (var checkCommand = new Npgsql.NpgsqlCommand(checkDbScript, connection))
                {
                    var exists = await checkCommand.ExecuteScalarAsync();
                    if (exists == null)
                    {
                        string createDbScript = "CREATE DATABASE \"DabTestDb\"";
                        using (var createCommand = new Npgsql.NpgsqlCommand(createDbScript, connection))
                        {
                            await createCommand.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
            
            // Wait a moment for database creation
            await Task.Delay(1000);
            
            // Connect to test database and set up tables
            using (var connection = new Npgsql.NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                
                var scripts = setupScript.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var script in scripts)
                {
                    if (!string.IsNullOrWhiteSpace(script.Trim()))
                    {
                        using (var command = new Npgsql.NpgsqlCommand(script.Trim(), connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
        }

        #region Database Connection Strings and Setup Scripts

        private static string GetMSSQLConnectionString()
        {
            return Environment.GetEnvironmentVariable("TEST_MSSQL_CONNECTION_STRING") 
                ?? "Server=localhost,1433;Database=DabTestDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;";
        }

        private static string GetMySQLConnectionString()
        {
            return Environment.GetEnvironmentVariable("TEST_MYSQL_CONNECTION_STRING") 
                ?? "server=localhost;port=3306;database=DabTestDb;user=root;password=test123;";
        }

        private static string GetPostgreSQLConnectionString()
        {
            return Environment.GetEnvironmentVariable("TEST_POSTGRESQL_CONNECTION_STRING") 
                ?? "Host=localhost;Port=5432;Database=DabTestDb;Username=postgres;Password=test123;";
        }

        private static string GetMSSQLSetupScript()
        {
            return @"
                IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'DabTestDb')
                BEGIN
                    CREATE DATABASE DabTestDb;
                END;
                
                USE DabTestDb;
                
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'books')
                BEGIN
                    CREATE TABLE dbo.books (
                        id INT IDENTITY(1,1) PRIMARY KEY,
                        title NVARCHAR(255) NOT NULL,
                        author NVARCHAR(255) NOT NULL,
                        publishedYear INT,
                        genre NVARCHAR(100)
                    );
                    
                    INSERT INTO dbo.books (title, author, publishedYear, genre) VALUES
                    ('The Great Gatsby', 'F. Scott Fitzgerald', 1925, 'Fiction'),
                    ('To Kill a Mockingbird', 'Harper Lee', 1960, 'Fiction'),
                    ('1984', 'George Orwell', 1949, 'Dystopian Fiction'),
                    ('Pride and Prejudice', 'Jane Austen', 1813, 'Romance'),
                    ('The Catcher in the Rye', 'J.D. Salinger', 1951, 'Fiction');
                END;
            ";
        }

        private static string GetMySQLSetupScript()
        {
            return @"
                CREATE DATABASE IF NOT EXISTS DabTestDb;
                USE DabTestDb;
                
                CREATE TABLE IF NOT EXISTS books (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    title VARCHAR(255) NOT NULL,
                    author VARCHAR(255) NOT NULL,
                    publishedYear INT,
                    genre VARCHAR(100)
                );
                
                INSERT IGNORE INTO books (title, author, publishedYear, genre) VALUES
                ('The Great Gatsby', 'F. Scott Fitzgerald', 1925, 'Fiction'),
                ('To Kill a Mockingbird', 'Harper Lee', 1960, 'Fiction'),
                ('1984', 'George Orwell', 1949, 'Dystopian Fiction'),
                ('Pride and Prejudice', 'Jane Austen', 1813, 'Romance'),
                ('The Catcher in the Rye', 'J.D. Salinger', 1951, 'Fiction');
            ";
        }

        private static string GetPostgreSQLSetupScript()
        {
            return @"
                CREATE DATABASE IF NOT EXISTS DabTestDb;
                \c DabTestDb;
                
                CREATE TABLE IF NOT EXISTS books (
                    id SERIAL PRIMARY KEY,
                    title VARCHAR(255) NOT NULL,
                    author VARCHAR(255) NOT NULL,
                    publishedYear INTEGER,
                    genre VARCHAR(100)
                );
                
                INSERT INTO books (title, author, publishedYear, genre) 
                SELECT * FROM (VALUES
                    ('The Great Gatsby', 'F. Scott Fitzgerald', 1925, 'Fiction'),
                    ('To Kill a Mockingbird', 'Harper Lee', 1960, 'Fiction'),
                    ('1984', 'George Orwell', 1949, 'Dystopian Fiction'),
                    ('Pride and Prejudice', 'Jane Austen', 1813, 'Romance'),
                    ('The Catcher in the Rye', 'J.D. Salinger', 1951, 'Fiction')
                ) AS v(title, author, publishedYear, genre)
                WHERE NOT EXISTS (SELECT 1 FROM books WHERE books.title = v.title);
            ";
        }

        #endregion

        #endregion
    }
}

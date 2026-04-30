// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for EmbeddingsCacheLevel2Options class.
/// Tests configuration, validation, and behavior of embeddings L2 Redis cache options.
/// </summary>
[TestClass]
public class EmbeddingsCacheLevel2OptionsTests
{
    private const string LocalRedisConnectionString = "localhost:6379";
    private const string AzureRedisConnectionString = "contoso.redis.cache.windows.net:6380,password=secretkey,ssl=True,abortConnect=False";
    private const string DifferentRedisConnectionString = "different:6379";
    private const string EnvironmentVariablePlaceholder = "@env('REDIS_CONNECTION_STRING')";
    private const string EmptyString = "";
    private const string WhitespaceString = "   ";

    [DataTestMethod]
    [DataRow(null, null, false, null, DisplayName = "Default values")]
    [DataRow(true, null, true, null, DisplayName = "Enabled without connection string")]
    [DataRow(true, LocalRedisConnectionString, true, LocalRedisConnectionString, DisplayName = "Local Redis")]
    [DataRow(true, AzureRedisConnectionString, true, AzureRedisConnectionString, DisplayName = "Azure Redis")]
    [DataRow(false, LocalRedisConnectionString, false, LocalRedisConnectionString, DisplayName = "Disabled with connection string")]
    [DataRow(true, EnvironmentVariablePlaceholder, true, EnvironmentVariablePlaceholder, DisplayName = "Environment variable")]
    [DataRow(true, EmptyString, true, EmptyString, DisplayName = "Empty connection string")]
    [DataRow(true, WhitespaceString, true, WhitespaceString, DisplayName = "Whitespace connection string")]
    public void EmbeddingsCacheLevel2Options_Configuration(bool? enabled, string connectionString, bool expectedEnabled, string expectedConnectionString)
    {
        // Arrange & Act
        EmbeddingsCacheLevel2Options options = new(
            Enabled: enabled,
            ConnectionString: connectionString);

        // Assert
        if (enabled == null)
        {
            Assert.IsFalse(options.Enabled ?? false, "Enabled should default to false when null");
        }
        else
        {
            Assert.AreEqual(expectedEnabled, options.Enabled ?? false, $"Enabled should be {expectedEnabled}");
        }

        Assert.AreEqual(expectedConnectionString, options.ConnectionString, "ConnectionString should match expected value");
    }

    [TestMethod]
    public void EmbeddingsCacheLevel2Options_RecordEquality()
    {
        // Arrange
        EmbeddingsCacheLevel2Options options1 = new(Enabled: true, ConnectionString: LocalRedisConnectionString);
        EmbeddingsCacheLevel2Options options2 = new(Enabled: true, ConnectionString: LocalRedisConnectionString);
        EmbeddingsCacheLevel2Options options3 = new(Enabled: true, ConnectionString: DifferentRedisConnectionString);

        // Act & Assert
        Assert.AreEqual(options1, options2, "Options with same values should be equal");
        Assert.AreNotEqual(options1, options3, "Options with different connection strings should not be equal");
    }
}

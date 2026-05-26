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
    private const string LOCAL_REDIS_CONNECTION_STRING = "localhost:6379";
    private const string AZURE_REDIS_CONNECTION_STRING = "contoso.redis.cache.windows.net:6380,password=secretkey,ssl=True,abortConnect=False";
    private const string DIFFERENT_REDIS_CONNECTION_STRING = "different:6379";
    private const string ENVIRONMENT_VARIABLE_PLACEHOLDER = "@env('REDIS_CONNECTION_STRING')";
    private const string EMPTY_STRING = "";
    private const string WHITESPACE_STRING = "   ";

    [DataTestMethod]
    [DataRow(null, null, false, null, DisplayName = "Default values")]
    [DataRow(true, null, true, null, DisplayName = "Enabled without connection string")]
    [DataRow(true, LOCAL_REDIS_CONNECTION_STRING, true, LOCAL_REDIS_CONNECTION_STRING, DisplayName = "Local Redis")]
    [DataRow(true, AZURE_REDIS_CONNECTION_STRING, true, AZURE_REDIS_CONNECTION_STRING, DisplayName = "Azure Redis")]
    [DataRow(false, LOCAL_REDIS_CONNECTION_STRING, false, LOCAL_REDIS_CONNECTION_STRING, DisplayName = "Disabled with connection string")]
    [DataRow(true, ENVIRONMENT_VARIABLE_PLACEHOLDER, true, ENVIRONMENT_VARIABLE_PLACEHOLDER, DisplayName = "Environment variable")]
    [DataRow(true, EMPTY_STRING, true, EMPTY_STRING, DisplayName = "Empty connection string")]
    [DataRow(true, WHITESPACE_STRING, true, WHITESPACE_STRING, DisplayName = "Whitespace connection string")]
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
        EmbeddingsCacheLevel2Options options1 = new(Enabled: true, ConnectionString: LOCAL_REDIS_CONNECTION_STRING);
        EmbeddingsCacheLevel2Options options2 = new(Enabled: true, ConnectionString: LOCAL_REDIS_CONNECTION_STRING);
        EmbeddingsCacheLevel2Options options3 = new(Enabled: true, ConnectionString: DIFFERENT_REDIS_CONNECTION_STRING);

        // Act & Assert
        Assert.AreEqual(options1, options2, "Options with same values should be equal");
        Assert.AreNotEqual(options1, options3, "Options with different connection strings should not be equal");
    }
}

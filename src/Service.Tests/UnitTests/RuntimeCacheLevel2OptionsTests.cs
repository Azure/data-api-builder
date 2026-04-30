// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for RuntimeCacheLevel2Options class.
/// Tests configuration, validation, and behavior of L2 Redis cache options.
/// </summary>
[TestClass]
public class RuntimeCacheLevel2OptionsTests
{
    private const string LocalRedisConnectionString = "localhost:6379";
    private const string AzureRedisConnectionString = "contoso.redis.cache.windows.net:6380,password=key,ssl=True,abortConnect=False";
    private const string DifferentRedisConnectionString = "different:6379";

    [DataTestMethod]
    [DataRow(null, null, false, null, DisplayName = "Default values")]
    [DataRow(true, null, true, null, DisplayName = "Enabled true without connection string")]
    [DataRow(true, LocalRedisConnectionString, true, LocalRedisConnectionString, DisplayName = "Local Redis connection")]
    [DataRow(true, AzureRedisConnectionString, true, AzureRedisConnectionString, DisplayName = "Azure Redis connection")]
    [DataRow(false, LocalRedisConnectionString, false, LocalRedisConnectionString, DisplayName = "Disabled with connection string")]
    [DataRow(null, null, false, null, DisplayName = "Null values")]
    public void RuntimeCacheLevel2Options_Configuration(bool? enabled, string connectionString, bool expectedEnabled, string expectedConnectionString)
    {
        // Arrange & Act
        RuntimeCacheLevel2Options options = new(
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
    public void RuntimeCacheLevel2Options_RecordEquality()
    {
        // Arrange
        RuntimeCacheLevel2Options options1 = new(Enabled: true, ConnectionString: LocalRedisConnectionString);
        RuntimeCacheLevel2Options options2 = new(Enabled: true, ConnectionString: LocalRedisConnectionString);
        RuntimeCacheLevel2Options options3 = new(Enabled: true, ConnectionString: DifferentRedisConnectionString);

        // Act & Assert
        Assert.AreEqual(options1, options2, "Options with same values should be equal");
        Assert.AreNotEqual(options1, options3, "Options with different values should not be equal");
    }
}

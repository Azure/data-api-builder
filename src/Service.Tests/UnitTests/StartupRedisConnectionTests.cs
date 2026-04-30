// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using Azure.DataApiBuilder.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for Redis connection logic in Startup.cs
/// Tests Azure Entra authentication detection and connection string handling.
/// </summary>
[TestClass]
public class StartupRedisConnectionTests
{
    private const string LocalhostConnectionString = "localhost:6379";
    private const string LocalhostWithPassword = "localhost:6379,password=secret";
    private const string LocalhostUppercase = "LOCALHOST:6379";
    private const string ContosoRedisHost = "contoso.redis.cache.windows.net";
    private const string ContosoRedisConnectionString = "contoso.redis.cache.windows.net:6380";
    private const string ContosoRedisWithPassword = "contoso.redis.cache.windows.net:6380,password=secret";
    private const string ContosoRedisEmptyPassword = "contoso.redis.cache.windows.net:6380,password=";
    private const string AzureRedisConnectionString = "myredis.redis.cache.windows.net:6380,ssl=True,abortConnect=False";
    private const int RedisPort = 6379;
    private const int AzureRedisPort = 6380;
    private const string RemoteIpAddress = "10.0.0.1";

    [DataTestMethod]
    [DataRow(LocalhostWithPassword, false, DisplayName = "With password - should not use Entra")]
    [DataRow(LocalhostConnectionString, false, DisplayName = "Localhost without password - should not use Entra")]
    [DataRow(LocalhostUppercase, false, DisplayName = "Case insensitive localhost - should not use Entra")]
    [DataRow(ContosoRedisConnectionString, true, DisplayName = "Remote without password - should use Entra")]
    [DataRow(ContosoRedisWithPassword, false, DisplayName = "Remote with password - should not use Entra")]
    [DataRow(ContosoRedisEmptyPassword, true, DisplayName = "Empty password - should use Entra")]
    [DataRow(AzureRedisConnectionString, true, DisplayName = "Azure Redis without password - should use Entra")]
    public void ShouldUseEntraAuthForRedis_ConnectionStringScenarios(string connectionString, bool expectedResult)
    {
        // Arrange
        ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);

        // Act
        bool result = Startup.ShouldUseEntraAuthForRedis(options);

        // Assert
        Assert.AreEqual(expectedResult, result);
    }

    [TestMethod]
    public void ShouldUseEntraAuthForRedis_LoopbackIP_ReturnsFalse()
    {
        // Arrange
        ConfigurationOptions options = new ConfigurationOptions
        {
            EndPoints = { new IPEndPoint(IPAddress.Loopback, RedisPort) }
        };

        // Act
        bool result = Startup.ShouldUseEntraAuthForRedis(options);

        // Assert
        Assert.IsFalse(result, "Should not use Entra auth for IPv4 loopback");
    }

    [TestMethod]
    public void ShouldUseEntraAuthForRedis_IPv6Loopback_ReturnsFalse()
    {
        // Arrange
        ConfigurationOptions options = new ConfigurationOptions
        {
            EndPoints = { new IPEndPoint(IPAddress.IPv6Loopback, RedisPort) }
        };

        // Act
        bool result = Startup.ShouldUseEntraAuthForRedis(options);

        // Assert
        Assert.IsFalse(result, "Should not use Entra auth for IPv6 loopback");
    }

    [TestMethod]
    public void ShouldUseEntraAuthForRedis_RemoteIP_ReturnsTrue()
    {
        // Arrange
        ConfigurationOptions options = new ConfigurationOptions
        {
            EndPoints = { new IPEndPoint(IPAddress.Parse(RemoteIpAddress), RedisPort) }
        };

        // Act
        bool result = Startup.ShouldUseEntraAuthForRedis(options);

        // Assert
        Assert.IsTrue(result, "Should use Entra auth for remote IP without password");
    }

    [TestMethod]
    public void ShouldUseEntraAuthForRedis_MixedEndpoints_ReturnsTrue()
    {
        // Arrange
        ConfigurationOptions options = new ConfigurationOptions
        {
            EndPoints = 
            { 
                new IPEndPoint(IPAddress.Loopback, RedisPort),
                new DnsEndPoint(ContosoRedisHost, AzureRedisPort)
            }
        };

        // Act
        bool result = Startup.ShouldUseEntraAuthForRedis(options);

        // Assert
        Assert.IsTrue(result, "Should use Entra auth when at least one endpoint is not localhost");
    }

    [TestMethod]
    public void ShouldUseEntraAuthForRedis_MultipleLocalhostEndpoints_ReturnsFalse()
    {
        // Arrange
        ConfigurationOptions options = new ConfigurationOptions
        {
            EndPoints = 
            { 
                new DnsEndPoint("localhost", RedisPort),
                new IPEndPoint(IPAddress.Loopback, AzureRedisPort)
            }
        };

        // Act
        bool result = Startup.ShouldUseEntraAuthForRedis(options);

        // Assert
        Assert.IsFalse(result, "Should not use Entra auth when all endpoints are localhost");
    }
}

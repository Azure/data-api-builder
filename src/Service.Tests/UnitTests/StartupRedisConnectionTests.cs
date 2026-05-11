// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
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
    private const string LOCALHOST_CONNECTION_STRING = "localhost:6379";
    private const string LOCALHOST_WITH_PASSWORD = "localhost:6379,password=secret";
    private const string LOCALHOST_UPPERCASE = "LOCALHOST:6379";
    private const string CONTOSO_REDIS_HOST = "contoso.redis.cache.windows.net";
    private const string CONTOSO_REDIS_CONNECTION_STRING = "contoso.redis.cache.windows.net:6380";
    private const string CONTOSO_REDIS_WITH_PASSWORD = "contoso.redis.cache.windows.net:6380,password=secret";
    private const string CONTOSO_REDIS_EMPTY_PASSWORD = "contoso.redis.cache.windows.net:6380,password=";
    private const string AZURE_REDIS_CONNECTION_STRING = "myredis.redis.cache.windows.net:6380,ssl=True,abortConnect=False";
    private const int REDIS_PORT = 6379;
    private const int AZURE_REDIS_PORT = 6380;
    private const string REMOTE_IP_ADDRESS = "10.0.0.1";

    [DataTestMethod]
    [DataRow(LOCALHOST_WITH_PASSWORD, false, DisplayName = "With password - should not use Entra")]
    [DataRow(LOCALHOST_CONNECTION_STRING, false, DisplayName = "Localhost without password - should not use Entra")]
    [DataRow(LOCALHOST_UPPERCASE, false, DisplayName = "Case insensitive localhost - should not use Entra")]
    [DataRow(CONTOSO_REDIS_CONNECTION_STRING, true, DisplayName = "Remote without password - should use Entra")]
    [DataRow(CONTOSO_REDIS_WITH_PASSWORD, false, DisplayName = "Remote with password - should not use Entra")]
    [DataRow(CONTOSO_REDIS_EMPTY_PASSWORD, true, DisplayName = "Empty password - should use Entra")]
    [DataRow(AZURE_REDIS_CONNECTION_STRING, true, DisplayName = "Azure Redis without password - should use Entra")]
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
        ConfigurationOptions options = new()
        {
            EndPoints = { new IPEndPoint(IPAddress.Loopback, REDIS_PORT) }
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
        ConfigurationOptions options = new()
        {
            EndPoints = { new IPEndPoint(IPAddress.IPv6Loopback, REDIS_PORT) }
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
        ConfigurationOptions options = new()
        {
            EndPoints = { new IPEndPoint(IPAddress.Parse(REMOTE_IP_ADDRESS), REDIS_PORT) }
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
        ConfigurationOptions options = new()
        {
            EndPoints =
            {
                new IPEndPoint(IPAddress.Loopback, REDIS_PORT),
                new DnsEndPoint(CONTOSO_REDIS_HOST, AZURE_REDIS_PORT)
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
        ConfigurationOptions options = new()
        {
            EndPoints =
            {
                new DnsEndPoint("localhost", REDIS_PORT),
                new IPEndPoint(IPAddress.Loopback, AZURE_REDIS_PORT)
            }
        };

        // Act
        bool result = Startup.ShouldUseEntraAuthForRedis(options);

        // Assert
        Assert.IsFalse(result, "Should not use Entra auth when all endpoints are localhost");
    }
}

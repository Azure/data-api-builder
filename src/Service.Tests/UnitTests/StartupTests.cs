// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class StartupTests
    {
        [DataTestMethod]
        [DataRow("localhost:6379", false, DisplayName = "Localhost endpoint without password should NOT use Entra auth.")]
        [DataRow("127.0.0.1:6379", false, DisplayName = "IPv4 loopback without password should NOT use Entra auth.")]
        [DataRow("[::1]:6379", false, DisplayName = "IPv6 loopback without password should NOT use Entra auth.")]
        [DataRow("redis.example.com:6380", true, DisplayName = "Remote endpoint without password SHOULD use Entra auth.")]
        [DataRow("redis.example.com:6380,password=secret", false, DisplayName = "Presence of password should NOT use Entra auth, even for remote endpoints.")]
        [DataRow("localhost:6379,redis.example.com:6380", true, DisplayName = "Mixed endpoints (including remote) without password SHOULD use Entra auth.")]
        [DataRow("localhost:6379,password=secret", false, DisplayName = "Localhost with password should NOT use Entra auth.")]
        public void ShouldUseEntraAuthForRedis(string connectionString, bool expectedUseEntraAuth)
        {
            // Arrange
            var options = ConfigurationOptions.Parse(connectionString);

            // Act
            bool result = Startup.ShouldUseEntraAuthForRedis(options);

            // Assert
            Assert.AreEqual(expectedUseEntraAuth, result);
        }
    }
}

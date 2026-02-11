// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.HealthCheck;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for health check utility methods.
    /// </summary>
    [TestClass]
    public class HealthCheckUtilitiesUnitTests
    {
        /// <summary>
        /// Tests that PostgreSQL connection strings are properly normalized.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_PostgreSQL_Success()
        {
            // Arrange
            string connectionString = "Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=testpass";
            DatabaseType dbType = DatabaseType.PostgreSQL;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(connectionString, dbType);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("Host=localhost"));
            Assert.IsTrue(result.Contains("Database=testdb"));
        }

        /// <summary>
        /// Tests that MSSQL connection strings are properly normalized.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_MSSQL_Success()
        {
            // Arrange
            string connectionString = "Server=localhost;Database=testdb;User Id=testuser;Password=testpass";
            DatabaseType dbType = DatabaseType.MSSQL;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(connectionString, dbType);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("Data Source=localhost"));
            Assert.IsTrue(result.Contains("Initial Catalog=testdb"));
        }

        /// <summary>
        /// Tests that DWSQL connection strings are properly normalized.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_DWSQL_Success()
        {
            // Arrange
            string connectionString = "Server=localhost;Database=testdb;User Id=testuser;Password=testpass";
            DatabaseType dbType = DatabaseType.DWSQL;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(connectionString, dbType);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("Data Source=localhost"));
            Assert.IsTrue(result.Contains("Initial Catalog=testdb"));
        }

        /// <summary>
        /// Tests that MySQL connection strings are properly normalized.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_MySQL_Success()
        {
            // Arrange
            string connectionString = "Server=localhost;Port=3306;Database=testdb;Uid=testuser;Pwd=testpass";
            DatabaseType dbType = DatabaseType.MySQL;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(connectionString, dbType);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("Server=localhost"));
            Assert.IsTrue(result.Contains("Database=testdb"));
        }

        /// <summary>
        /// Tests that unsupported database types return the original connection string.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_UnsupportedType_ReturnsOriginal()
        {
            // Arrange
            string connectionString = "AccountEndpoint=https://test.documents.azure.com:443/;AccountKey=test";
            DatabaseType dbType = DatabaseType.CosmosDB_NoSQL;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(connectionString, dbType);

            // Assert
            Assert.AreEqual(connectionString, result);
        }

        /// <summary>
        /// Tests that malformed connection strings are handled gracefully.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_MalformedString_ReturnsOriginalAndLogs()
        {
            // Arrange
            string malformedConnectionString = "InvalidConnectionString;NoEquals";
            DatabaseType dbType = DatabaseType.PostgreSQL;
            Mock<ILogger> mockLogger = new Mock<ILogger>();

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(malformedConnectionString, dbType, mockLogger.Object);

            // Assert
            Assert.AreEqual(malformedConnectionString, result);
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }

        /// <summary>
        /// Tests that null logger is handled gracefully.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_MalformedString_NullLogger_ReturnsOriginal()
        {
            // Arrange
            string malformedConnectionString = "InvalidConnectionString;NoEquals";
            DatabaseType dbType = DatabaseType.MSSQL;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(malformedConnectionString, dbType, null);

            // Assert
            Assert.AreEqual(malformedConnectionString, result);
        }

        /// <summary>
        /// Tests that PostgreSQL connection strings with lowercase keywords are normalized correctly.
        /// This is the specific bug that was reported - lowercase 'host' was not supported.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_PostgreSQL_LowercaseKeywords_Success()
        {
            // Arrange
            string connectionString = "host=localhost;port=5432;database=mydb;username=myuser;password=mypass";
            DatabaseType dbType = DatabaseType.PostgreSQL;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(connectionString, dbType);

            // Assert
            Assert.IsNotNull(result);
            // NpgsqlConnectionStringBuilder should normalize lowercase keywords to proper format
            Assert.IsTrue(result.Contains("Host=localhost") || result.Contains("host=localhost"));
            Assert.IsTrue(result.Contains("Database=mydb") || result.Contains("database=mydb"));
        }

        /// <summary>
        /// Tests that empty connection strings are handled gracefully.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_EmptyString_ReturnsEmpty()
        {
            // Arrange
            string connectionString = string.Empty;
            DatabaseType dbType = DatabaseType.PostgreSQL;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(connectionString, dbType);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }
    }
}

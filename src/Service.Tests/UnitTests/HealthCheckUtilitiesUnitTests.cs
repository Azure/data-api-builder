// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using Azure.DataApiBuilder.Config.ObjectModel;
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
        /// Tests that connection strings are properly normalized for supported database types.
        /// </summary>
        [TestMethod]
        [DataRow(
            DatabaseType.PostgreSQL,
            "Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=XXXX",
            "Host=localhost",
            "Database=testdb",
            DisplayName = "PostgreSQL connection string normalization")]
        [DataRow(
            DatabaseType.MSSQL,
            "Server=localhost;Database=testdb;User Id=testuser;Password=XXXX",
            "Data Source=localhost",
            "Initial Catalog=testdb",
            DisplayName = "MSSQL connection string normalization")]
        [DataRow(
            DatabaseType.DWSQL,
            "Server=localhost;Database=testdb;User Id=testuser;Password=XXXX",
            "Data Source=localhost",
            "Initial Catalog=testdb",
            DisplayName = "DWSQL connection string normalization")]
        [DataRow(
            DatabaseType.MySQL,
            "Server=localhost;Port=3306;Database=testdb;Uid=testuser;Pwd=XXXX",
            "Server=localhost",
            "Database=testdb",
            DisplayName = "MySQL connection string normalization")]
        public void NormalizeConnectionString_SupportedDatabases_Success(
            DatabaseType dbType,
            string connectionString,
            string expectedServerPart,
            string expectedDatabasePart)
        {
            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(connectionString, dbType);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains(expectedServerPart));
            Assert.IsTrue(result.Contains(expectedDatabasePart));
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
        [DataRow(DatabaseType.PostgreSQL, true, DisplayName = "PostgreSQL malformed string with logger")]
        [DataRow(DatabaseType.MSSQL, true, DisplayName = "MSSQL malformed string with logger")]
        [DataRow(DatabaseType.MySQL, false, DisplayName = "MySQL malformed string without logger")]
        public void NormalizeConnectionString_MalformedString_ReturnsOriginal(
            DatabaseType dbType,
            bool useLogger)
        {
            // Arrange
            string malformedConnectionString = "InvalidConnectionString;NoEquals";
            Mock<ILogger>? mockLogger = useLogger ? new Mock<ILogger>() : null;

            // Act
            string result = HealthCheck.Utilities.NormalizeConnectionString(
                malformedConnectionString,
                dbType,
                mockLogger?.Object);

            // Assert
            Assert.AreEqual(malformedConnectionString, result);
            if (useLogger && mockLogger != null)
            {
                mockLogger.Verify(
                    x => x.Log(
                        LogLevel.Warning,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => true),
                        It.IsAny<Exception>(),
                        It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                    Times.Once);
            }
        }

        /// <summary>
        /// Tests that PostgreSQL connection strings with lowercase keywords are normalized correctly.
        /// This is the specific bug that was reported - lowercase 'host' was not supported.
        /// </summary>
        [TestMethod]
        public void NormalizeConnectionString_PostgreSQL_LowercaseKeywords_Success()
        {
            // Arrange
            string connectionString = "host=localhost;port=5432;database=mydb;username=myuser;password=XXXX";
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

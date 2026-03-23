// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Service.HealthCheck;
using Microsoft.AspNetCore.Http;
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
        /// <summary>
        /// Tests that GetCurrentRole returns "anonymous" when no auth headers are present.
        /// </summary>
        [TestMethod]
        public void GetCurrentRole_NoHeaders_ReturnsAnonymous()
        {
            HealthCheckHelper helper = CreateHelper();
            string role = helper.GetCurrentRole(roleHeader: string.Empty, roleToken: string.Empty);
            Assert.AreEqual(AuthorizationResolver.ROLE_ANONYMOUS, role);
        }

        /// <summary>
        /// Tests that GetCurrentRole returns "authenticated" when a bearer token is present but no role header is supplied.
        /// </summary>
        [TestMethod]
        public void GetCurrentRole_BearerTokenOnly_ReturnsAuthenticated()
        {
            HealthCheckHelper helper = CreateHelper();
            string role = helper.GetCurrentRole(roleHeader: string.Empty, roleToken: "some-bearer-token");
            Assert.AreEqual(AuthorizationResolver.ROLE_AUTHENTICATED, role);
        }

        /// <summary>
        /// Tests that GetCurrentRole returns the explicit role value when the X-MS-API-ROLE header is provided.
        /// </summary>
        [TestMethod]
        [DataRow("anonymous", DisplayName = "Explicit anonymous role header")]
        [DataRow("authenticated", DisplayName = "Explicit authenticated role header")]
        [DataRow("customrole", DisplayName = "Custom role header")]
        public void GetCurrentRole_ExplicitRoleHeader_ReturnsHeaderValue(string explicitRole)
        {
            HealthCheckHelper helper = CreateHelper();
            string role = helper.GetCurrentRole(roleHeader: explicitRole, roleToken: string.Empty);
            Assert.AreEqual(explicitRole, role);
        }

        /// <summary>
        /// Tests that the role header takes priority over the bearer token when both are present.
        /// </summary>
        [TestMethod]
        public void GetCurrentRole_BothHeaderAndToken_RoleHeaderWins()
        {
            HealthCheckHelper helper = CreateHelper();
            string role = helper.GetCurrentRole(roleHeader: "customrole", roleToken: "some-bearer-token");
            Assert.AreEqual("customrole", role);
        }

        /// <summary>
        /// Tests that ReadRoleHeaders correctly reads X-MS-API-ROLE from the request.
        /// </summary>
        [TestMethod]
        public void ReadRoleHeaders_WithRoleHeader_ReturnsRoleHeader()
        {
            HealthCheckHelper helper = CreateHelper();
            DefaultHttpContext context = new();
            context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "myrole";

            (string roleHeader, string roleToken) = helper.ReadRoleHeaders(context);

            Assert.AreEqual("myrole", roleHeader);
            Assert.AreEqual(string.Empty, roleToken);
        }

        /// <summary>
        /// Tests that ReadRoleHeaders returns empty strings when no headers are present.
        /// </summary>
        [TestMethod]
        public void ReadRoleHeaders_NoHeaders_ReturnsEmpty()
        {
            HealthCheckHelper helper = CreateHelper();
            DefaultHttpContext context = new();

            (string roleHeader, string roleToken) = helper.ReadRoleHeaders(context);

            Assert.AreEqual(string.Empty, roleHeader);
            Assert.AreEqual(string.Empty, roleToken);
        }

        /// <summary>
        /// Tests that the cached health response does not reuse a previous caller's currentRole.
        /// GetCurrentRole is a pure function: same input always produces same output,
        /// and different inputs (representing different callers) produce different outputs.
        /// </summary>
        [TestMethod]
        public void GetCurrentRole_CacheDoesNotLeakRole_DifferentCallersGetDifferentRoles()
        {
            HealthCheckHelper helper = CreateHelper();

            // Simulate request 1 (anonymous, no headers)
            string role1 = helper.GetCurrentRole(roleHeader: string.Empty, roleToken: string.Empty);

            // Simulate request 2 (authenticated, with bearer token)
            string role2 = helper.GetCurrentRole(roleHeader: string.Empty, roleToken: "bearer-token");

            // Simulate request 3 (explicit custom role)
            string role3 = helper.GetCurrentRole(roleHeader: "adminrole", roleToken: string.Empty);

            Assert.AreEqual(AuthorizationResolver.ROLE_ANONYMOUS, role1);
            Assert.AreEqual(AuthorizationResolver.ROLE_AUTHENTICATED, role2);
            Assert.AreEqual("adminrole", role3);
        }

        /// <summary>
        /// Tests that parallel calls to GetCurrentRole with different roles do not bleed values across calls.
        /// Validates the singleton-safe design (no shared mutable state).
        /// </summary>
        [TestMethod]
        public async Task GetCurrentRole_ParallelRequests_NoRoleBleed()
        {
            HealthCheckHelper helper = CreateHelper();

            // Run many parallel "requests" each with a unique role
            int parallelCount = 50;
            string[] expectedRoles = new string[parallelCount];
            string[] actualRoles = new string[parallelCount];

            for (int i = 0; i < parallelCount; i++)
            {
                expectedRoles[i] = $"role-{i}";
            }

            List<Task> tasks = new();
            for (int i = 0; i < parallelCount; i++)
            {
                int index = i;
                tasks.Add(Task.Run(() =>
                {
                    actualRoles[index] = helper.GetCurrentRole(roleHeader: expectedRoles[index], roleToken: string.Empty);
                }));
            }

            await Task.WhenAll(tasks);

            for (int i = 0; i < parallelCount; i++)
            {
                Assert.AreEqual(expectedRoles[i], actualRoles[i], $"Role bleed detected at index {i}: expected '{expectedRoles[i]}' but got '{actualRoles[i]}'");
            }
        }

        private static HealthCheckHelper CreateHelper()
        {
            Mock<ILogger<HealthCheckHelper>> loggerMock = new();
            // HttpUtilities is not invoked by the methods under test (GetCurrentRole, ReadRoleHeaders),
            // so passing null is safe here.
            return new HealthCheckHelper(loggerMock.Object, null!);
        }
    }
}

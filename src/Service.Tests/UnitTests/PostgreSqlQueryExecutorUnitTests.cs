// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Npgsql;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlQueryExecutorUnitTests
    {
        [TestInitialize]
        public void TestInitialize()
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.POSTGRESQL);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Validates managed identity token issued ONLY when connection string does not specify password
        /// </summary>
        [DataTestMethod]
        [DataRow("Server =<>;Database=<>;Username=xyz;", false, false,
            DisplayName = "No managed identity access token even when connection string specifies Username only.")]
        [DataRow("Server =<>;Database=<>;Username=xyz;", true, false,
            DisplayName = "Managed identity access token from config used when connection string specifies Username only.")]
        [DataRow("Server =<>;Database=<>;Username=xyz;", true, true,
            DisplayName = "Default managed identity access token used when connection string specifies Username only.")]
        [DataRow("Server =<>;Database=<>;Password=xyz;", false, false,
            DisplayName = "No managed identity access token when connection string specifies Password only.")]
        [DataRow("Server =<>;Database=<>;Username=xyz;Password=xxx", false, false,
            DisplayName = "No managed identity access token when connection string specifies both Username and Password.")]
        public async Task TestHandleManagedIdentityAccess(
            string connectionString,
            bool expectManagedIdentityAccessToken,
            bool isDefaultAzureCredential)
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.PostgreSQL, connectionString, new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
            );

            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(mockConfig);
            Mock<DbExceptionParser> dbExceptionParser = new(provider);
            Mock<ILogger<PostgreSqlQueryExecutor>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            PostgreSqlQueryExecutor postgreSqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);

            const string DEFAULT_TOKEN = "Default access token";
            const string CONFIG_TOKEN = "Configuration controller access token";
            AccessToken testValidToken = new(accessToken: DEFAULT_TOKEN, expiresOn: DateTimeOffset.MaxValue);
            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Mock<DefaultAzureCredential> dacMock = new();
                    dacMock
                        .Setup(m => m.GetTokenAsync(It.IsAny<TokenRequestContext>(),
                            It.IsAny<System.Threading.CancellationToken>()))
                        .Returns(ValueTask.FromResult(testValidToken));
                    postgreSqlQueryExecutor.AzureCredential = dacMock.Object;
                }
                else
                {
                    await provider.Initialize(
                        provider.GetConfig().ToJson(),
                        graphQLSchema: null,
                        connectionString: connectionString,
                        accessToken: CONFIG_TOKEN,
                        replacementSettings: new());
                    postgreSqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);
                }
            }

            using NpgsqlConnection conn = new(connectionString);
            await postgreSqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, string.Empty);
            NpgsqlConnectionStringBuilder connStringBuilder = new(conn.ConnectionString);

            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Assert.AreEqual(expected: DEFAULT_TOKEN, actual: connStringBuilder.Password);
                }
                else
                {
                    Assert.AreEqual(expected: CONFIG_TOKEN, actual: connStringBuilder.Password);
                }
            }
            else
            {
                Assert.AreEqual(connectionString, conn.ConnectionString);
            }
        }

        #region PrepareDbCommand Tests

        /// <summary>
        /// Validates that PrepareDbCommand creates a command with the correct SQL text.
        /// </summary>
        [TestMethod]
        public void PrepareDbCommand_WithSqlText_SetsCommandTextCorrectly()
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            using NpgsqlConnection conn = new(connectionString);
            const string sqlText = "SELECT * FROM users WHERE id = @id";
            Dictionary<string, DbConnectionParam> parameters = new();

            // Act
            using NpgsqlCommand cmd = executor.PrepareDbCommand(conn, sqlText, parameters, null, string.Empty);

            // Assert
            Assert.AreEqual(CommandType.Text, cmd.CommandType);
            Assert.IsTrue(cmd.CommandText.EndsWith(sqlText));
        }

        /// <summary>
        /// Validates that PrepareDbCommand correctly adds parameters with their values.
        /// </summary>
        [TestMethod]
        public void PrepareDbCommand_WithParameters_AddsParametersCorrectly()
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            using NpgsqlConnection conn = new(connectionString);
            const string sqlText = "SELECT * FROM users WHERE id = @id AND name = @name";
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@id", new DbConnectionParam(123) },
                { "@name", new DbConnectionParam("TestUser") }
            };

            // Act
            using NpgsqlCommand cmd = executor.PrepareDbCommand(conn, sqlText, parameters, null, string.Empty);

            // Assert
            Assert.AreEqual(2, cmd.Parameters.Count);
            Assert.AreEqual(123, cmd.Parameters["@id"].Value);
            Assert.AreEqual("TestUser", cmd.Parameters["@name"].Value);
        }

        /// <summary>
        /// Validates that PrepareDbCommand correctly handles null parameter values by converting to DBNull.Value.
        /// </summary>
        [TestMethod]
        public void PrepareDbCommand_WithNullParameterValue_SetsDbNullValue()
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            using NpgsqlConnection conn = new(connectionString);
            const string sqlText = "SELECT * FROM users WHERE name = @name";
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@name", new DbConnectionParam(null) }
            };

            // Act
            using NpgsqlCommand cmd = executor.PrepareDbCommand(conn, sqlText, parameters, null, string.Empty);

            // Assert
            Assert.AreEqual(1, cmd.Parameters.Count);
            Assert.AreEqual(DBNull.Value, cmd.Parameters["@name"].Value);
        }

        /// <summary>
        /// Validates that PrepareDbCommand correctly sets DbType when provided in the parameter.
        /// </summary>
        [DataTestMethod]
        [DataRow(DbType.Date, DisplayName = "DbType.Date is set correctly")]
        [DataRow(DbType.DateTime, DisplayName = "DbType.DateTime is set correctly")]
        [DataRow(DbType.DateTime2, DisplayName = "DbType.DateTime2 is set correctly")]
        [DataRow(DbType.Time, DisplayName = "DbType.Time is set correctly")]
        [DataRow(DbType.Int32, DisplayName = "DbType.Int32 is set correctly")]
        [DataRow(DbType.String, DisplayName = "DbType.String is set correctly")]
        [DataRow(DbType.Boolean, DisplayName = "DbType.Boolean is set correctly")]
        [DataRow(DbType.Decimal, DisplayName = "DbType.Decimal is set correctly")]
        [DataRow(DbType.Guid, DisplayName = "DbType.Guid is set correctly")]
        public void PrepareDbCommand_WithDbType_SetsDbTypeCorrectly(DbType dbType)
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            using NpgsqlConnection conn = new(connectionString);
            const string sqlText = "SELECT * FROM events WHERE event_date = @eventDate";
            Dictionary<string, DbConnectionParam> parameters = new()
            {
                { "@eventDate", new DbConnectionParam(DateTime.Now, dbType) }
            };

            // Act
            using NpgsqlCommand cmd = executor.PrepareDbCommand(conn, sqlText, parameters, null, string.Empty);

            // Assert
            Assert.AreEqual(1, cmd.Parameters.Count);
            Assert.AreEqual(dbType, cmd.Parameters["@eventDate"].DbType);
        }

        /// <summary>
        /// Validates that PrepareDbCommand handles empty parameters dictionary correctly.
        /// </summary>
        [TestMethod]
        public void PrepareDbCommand_WithEmptyParameters_CreatesCommandWithNoParameters()
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            using NpgsqlConnection conn = new(connectionString);
            const string sqlText = "SELECT * FROM users";
            Dictionary<string, DbConnectionParam> parameters = new();

            // Act
            using NpgsqlCommand cmd = executor.PrepareDbCommand(conn, sqlText, parameters, null, string.Empty);

            // Assert
            Assert.AreEqual(0, cmd.Parameters.Count);
            Assert.IsTrue(cmd.CommandText.EndsWith(sqlText));
        }

        /// <summary>
        /// Validates that PrepareDbCommand handles null parameters dictionary correctly.
        /// </summary>
        [TestMethod]
        public void PrepareDbCommand_WithNullParameters_CreatesCommandWithNoParameters()
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            using NpgsqlConnection conn = new(connectionString);
            const string sqlText = "SELECT * FROM users";

            // Act
            using NpgsqlCommand cmd = executor.PrepareDbCommand(conn, sqlText, null!, null, string.Empty);

            // Assert
            Assert.AreEqual(0, cmd.Parameters.Count);
        }

        #endregion

        #region PopulateDbTypeForParameter Tests

        /// <summary>
        /// Validates that PopulateDbTypeForParameter sets the DbType when it is provided.
        /// </summary>
        [DataTestMethod]
        [DataRow(DbType.Date, DisplayName = "PopulateDbTypeForParameter sets DbType.Date")]
        [DataRow(DbType.DateTime, DisplayName = "PopulateDbTypeForParameter sets DbType.DateTime")]
        [DataRow(DbType.DateTime2, DisplayName = "PopulateDbTypeForParameter sets DbType.DateTime2")]
        [DataRow(DbType.DateTimeOffset, DisplayName = "PopulateDbTypeForParameter sets DbType.DateTimeOffset")]
        [DataRow(DbType.Time, DisplayName = "PopulateDbTypeForParameter sets DbType.Time")]
        [DataRow(DbType.Int16, DisplayName = "PopulateDbTypeForParameter sets DbType.Int16")]
        [DataRow(DbType.Int32, DisplayName = "PopulateDbTypeForParameter sets DbType.Int32")]
        [DataRow(DbType.Int64, DisplayName = "PopulateDbTypeForParameter sets DbType.Int64")]
        [DataRow(DbType.String, DisplayName = "PopulateDbTypeForParameter sets DbType.String")]
        [DataRow(DbType.Boolean, DisplayName = "PopulateDbTypeForParameter sets DbType.Boolean")]
        [DataRow(DbType.Double, DisplayName = "PopulateDbTypeForParameter sets DbType.Double")]
        [DataRow(DbType.Decimal, DisplayName = "PopulateDbTypeForParameter sets DbType.Decimal")]
        [DataRow(DbType.Guid, DisplayName = "PopulateDbTypeForParameter sets DbType.Guid")]
        [DataRow(DbType.Binary, DisplayName = "PopulateDbTypeForParameter sets DbType.Binary")]
        public void PopulateDbTypeForParameter_WithDbType_SetsDbTypeOnParameter(DbType expectedDbType)
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            KeyValuePair<string, DbConnectionParam> parameterEntry = new("@param", new DbConnectionParam("value", expectedDbType));
            using NpgsqlConnection conn = new(connectionString);
            using NpgsqlCommand cmd = conn.CreateCommand();
            DbParameter parameter = cmd.CreateParameter();

            // Act
            executor.PopulateDbTypeForParameter(parameterEntry, parameter);

            // Assert
            Assert.AreEqual(expectedDbType, parameter.DbType);
        }

        /// <summary>
        /// Validates that PopulateDbTypeForParameter does not modify DbType when it is null.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_WithNullDbType_DoesNotSetDbType()
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            KeyValuePair<string, DbConnectionParam> parameterEntry = new("@param", new DbConnectionParam("value", dbType: null));
            using NpgsqlConnection conn = new(connectionString);
            using NpgsqlCommand cmd = conn.CreateCommand();
            DbParameter parameter = cmd.CreateParameter();
            DbType originalDbType = parameter.DbType;

            // Act
            executor.PopulateDbTypeForParameter(parameterEntry, parameter);

            // Assert
            Assert.AreEqual(originalDbType, parameter.DbType);
        }

        /// <summary>
        /// Validates that PopulateDbTypeForParameter handles a null Value in DbConnectionParam correctly.
        /// The DbType should still be set if provided.
        /// </summary>
        [TestMethod]
        public void PopulateDbTypeForParameter_WithNullValueButDbType_SetsDbType()
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            KeyValuePair<string, DbConnectionParam> parameterEntry = new("@param", new DbConnectionParam(null, DbType.Date));
            using NpgsqlConnection conn = new(connectionString);
            using NpgsqlCommand cmd = conn.CreateCommand();
            DbParameter parameter = cmd.CreateParameter();

            // Act
            executor.PopulateDbTypeForParameter(parameterEntry, parameter);

            // Assert
            Assert.AreEqual(DbType.Date, parameter.DbType);
        }

        /// <summary>
        /// Validates that PopulateDbTypeForParameter correctly handles date types to prevent
        /// "operator does not exist: date >= text" errors.
        /// </summary>
        [DataTestMethod]
        [DataRow("2024-01-15", DbType.Date, DisplayName = "Date string with DbType.Date")]
        [DataRow("2024-01-15T10:30:00", DbType.DateTime, DisplayName = "DateTime string with DbType.DateTime")]
        [DataRow("10:30:00", DbType.Time, DisplayName = "Time string with DbType.Time")]
        public void PopulateDbTypeForParameter_WithDateTimeTypes_SetsCorrectDbType(string value, DbType expectedDbType)
        {
            // Arrange
            const string connectionString = "Server=localhost;Database=testdb;";
            PostgreSqlQueryExecutor executor = CreatePostgreSqlQueryExecutor(connectionString);
            KeyValuePair<string, DbConnectionParam> parameterEntry = new("@dateParam", new DbConnectionParam(value, expectedDbType));
            using NpgsqlConnection conn = new(connectionString);
            using NpgsqlCommand cmd = conn.CreateCommand();
            DbParameter parameter = cmd.CreateParameter();

            // Act
            executor.PopulateDbTypeForParameter(parameterEntry, parameter);

            // Assert
            Assert.AreEqual(expectedDbType, parameter.DbType);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a PostgreSqlQueryExecutor instance for testing.
        /// </summary>
        private static PostgreSqlQueryExecutor CreatePostgreSqlQueryExecutor(string connectionString)
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.PostgreSQL, connectionString, new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
            );

            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(mockConfig);
            Mock<DbExceptionParser> dbExceptionParser = new(provider);
            Mock<ILogger<PostgreSqlQueryExecutor>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();

            return new PostgreSqlQueryExecutor(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);
        }

        #endregion
    }
}


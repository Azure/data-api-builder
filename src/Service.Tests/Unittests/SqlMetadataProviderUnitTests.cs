// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Units testing for our connection string parser
    /// to retreive schema.
    /// </summary>
    [TestClass]
    public class SqlMetadataProviderUnitTests : SqlTestBase
    {
        /// <summary>
        /// Only for PostgreSql connection strings.
        /// Verify we parse the connection string for the
        /// schema correctly when it is of various relevant
        /// formats.
        /// </summary>
        [DataTestMethod]
        [DataRow("", "Host=localhost;Database=graphql;SearchPath=\"\"")]
        [DataRow("", "Host=localhost;Database=graphql;SearchPath=")]
        [DataRow("foobar", "Host=localhost;Database=graphql;SearchPath=foobar")]
        [DataRow("foobar", "Host=localhost;Database=graphql;SearchPath=\"foobar\"")]
        [DataRow("baz", "SearchPath=\"baz\";Host=localhost;Database=graphql")]
        [DataRow("baz", "SearchPath=baz;Host=localhost;Database=graphql")]
        [DataRow("", "Host=localhost;Database=graphql")]
        [DataRow("", "SearchPath=;Host=localhost;Database=graphql")]
        [DataRow("", "SearchPath=\"\";Host=localhost;Database=graphql")]
        public void CheckConnectionStringParsingTest(string expected, string connectionString)
        {
            PostgreSqlMetadataProvider.TryGetSchemaFromConnectionString(connectionString, out string actual);
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// <code>Do: </code> Fills the table definition with information of the foreign keys
        /// for all the tables based on the entities relationship.
        /// <code>Check: </code> Making sure no exception is thrown if there are no Foreign Keys.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.POSTGRESQL)]
        public async Task CheckNoExceptionForNoForeignKey()
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig();
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(runtimeConfig);
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetUpSQLMetadataProvider(runtimeConfigProvider);
            await ResetDbStateAsync();
            await _sqlMetadataProvider.InitializeAsync();
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set connection string and db type
        /// according to data row.
        /// <code>Check: </code>  Verify malformed connection string throws correct exception with MSSQL as the database.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow(";;;;;fooBarBAZ", true)]
        [DataRow("!&^%*&$$%#$%@$%#@()", true)]
        [DataRow("Server=<>;Databases=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;", true)]
        [DataRow("Servers=<>;Database=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;", true)]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests", true)]
        [DataRow("", false)]
        public async Task CheckExceptionForBadConnectionStringForMsSql(string connectionString, bool isInvalidConnectionBuilderString)
        {
            StringWriter sw = null;
            // For strings that are an invalid format for the connection string builder, need to
            // redirect std error to a string writer for comparison to expected error messaging later.
            if (isInvalidConnectionBuilderString)
            {
                sw = new();
                Console.SetError(sw);
            }

            DatabaseEngine = TestCategory.MSSQL;
            await CheckExceptionForBadConnectionStringHelperAsync(DatabaseEngine, connectionString, sw);
        }

        /// <summary>
        /// <code>Do: </code> Tests with different combinations of schema and table names
        /// to validate that the correct full table name with schema as prefix is generated. For example if
        /// schemaName = model, and tableName = TrainedModel, then correct would mean
        /// [model].[TrainedModel], and any other form would be incorrect.
        /// <code>Check: </code> Making sure table name with prefix matches expected name with prefix.
        /// </summary>
        [DataTestMethod]
        [DataRow("", "", "[]")]
        [DataRow("model", "TrainedModel", "[model].[TrainedModel]")]
        [DataRow("", "TestTable", "[TestTable]")]
        [DataRow("model", "TrainedModel", "[model].[TrainedModel]")]
        public void CheckTablePrefix(string schemaName, string tableName, string expectedTableNameWithPrefix)
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            RuntimeConfig baseConfigFromDisk = SqlTestHelper.SetupRuntimeConfig();
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(baseConfigFromDisk);
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
            string dataSourceName = runtimeConfig.GetDefaultDataSourceName();

            ILogger<ISqlMetadataProvider> sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;
            Mock<IQueryExecutor> queryExecutor = new();
            IQueryBuilder queryBuilder = new MsSqlQueryBuilder();

            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(queryBuilder);
            queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(queryExecutor.Object);

            SqlMetadataProvider<SqlConnection, SqlDataAdapter, SqlCommand> provider = new MsSqlMetadataProvider(
                runtimeConfigProvider,
                queryManagerFactory.Object,
                sqlMetadataLogger,
                dataSourceName);
            string tableNameWithPrefix = provider.GetTableNameWithSchemaPrefix(schemaName, tableName);
            Assert.AreEqual(expectedTableNameWithPrefix, tableNameWithPrefix);
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set connection string and db type
        /// according to data row.
        /// <code>Check: </code>  Verify malformed connection string throws correct exception with MySQL as the database.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MYSQL)]
        [DataRow(";;;;;fooBarBAZ")]
        [DataRow("!&^%*&$$%#$%@$%#@()")]
        [DataRow("Server=<>;Databases=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;")]
        [DataRow("Servers=<>;Database=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;")]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests")]
        [DataRow("")]
        public async Task CheckExceptionForBadConnectionStringForMySql(string connectionString)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await CheckExceptionForBadConnectionStringHelperAsync(DatabaseEngine, connectionString);
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set connection string and db type
        /// according to data row.
        /// <code>Check: </code>  Verify malformed connection string throws correct exception with PostgreSQL as the database.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.POSTGRESQL)]
        [DataRow(";;;;;fooBarBAZ")]
        [DataRow("!&^%*&$$%#$%@$%#@()")]
        [DataRow("Server=<>;Databases=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;")]
        [DataRow("Servers=<>;Database=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;")]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests")]
        [DataRow("")]
        public async Task CheckExceptionForBadConnectionStringForPgSql(string connectionString)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await CheckExceptionForBadConnectionStringHelperAsync(DatabaseEngine, connectionString);
        }

        /// <summary>
        /// Helper method to validate the exception message when malformed connection strings are used
        /// to retrieve metadata information from the database
        /// </summary>
        /// <param name="databaseType"></param>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        private static async Task CheckExceptionForBadConnectionStringHelperAsync(string databaseType, string connectionString, StringWriter sw = null)
        {
            TestHelper.SetupDatabaseEnvironment(databaseType);
            RuntimeConfig baseConfigFromDisk = SqlTestHelper.SetupRuntimeConfig();

            RuntimeConfig runtimeConfig = baseConfigFromDisk with { DataSource = baseConfigFromDisk.DataSource with { ConnectionString = connectionString } };
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            ILogger<ISqlMetadataProvider> sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;

            // MySQL test will not error out before calling the query builder's format function and
            // therefore can not be null
            if (string.Equals(databaseType, TestCategory.MYSQL))
            {
                _queryBuilder = new MySqlQueryBuilder();
            }

            try
            {
                string dataSourceName = runtimeConfigProvider.GetConfig().DefaultDataSourceName;
                // Setup Mock query manager Factory
                Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
                queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(_queryBuilder);
                queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(_queryExecutor);

                ISqlMetadataProvider sqlMetadataProvider = databaseType switch
                {
                    TestCategory.MSSQL => new MsSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory.Object, sqlMetadataLogger, dataSourceName),
                    TestCategory.MYSQL => new MySqlMetadataProvider(runtimeConfigProvider, queryManagerFactory.Object, sqlMetadataLogger, dataSourceName),
                    TestCategory.POSTGRESQL => new PostgreSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory.Object, sqlMetadataLogger, dataSourceName),
                    _ => throw new ArgumentException($"Invalid database type: {databaseType}")
                };

                await sqlMetadataProvider.InitializeAsync();
            }
            catch (DataApiBuilderException ex)
            {
                // use contains to correctly cover db/user unique error messaging
                // if sw is not null it holds the error messaging
                string error = sw is null ? ex.Message : sw.ToString();
                Assert.IsTrue(error.Contains(DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE));
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                if (sw is not null)
                {
                    Assert.IsTrue(error.StartsWith("Deserialization of the configuration file failed during a post-processing step."));
                }
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set up the source fields for the entities.
        /// <code>Check: </code>  Verifies that source object is correctly parsed.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task CheckCorrectParsingForStoredProcedure()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig();
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetUpSQLMetadataProvider(runtimeConfigProvider);

            await _sqlMetadataProvider.InitializeAsync();

            Entity entity = runtimeConfig.Entities["GetBooks"];
            Assert.AreEqual("get_books", entity.Source.Object);
            Assert.AreEqual(EntitySourceType.StoredProcedure, entity.Source.Type);

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set up the source fields for the entities.
        /// <code>Check: </code>  Verifies that source object is correctly parsed.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task CheckGetFieldMappings()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig();
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetUpSQLMetadataProvider(runtimeConfigProvider);

            await _sqlMetadataProvider.InitializeAsync();

            MsSqlMetadataProvider metadataProvider = (MsSqlMetadataProvider)_sqlMetadataProvider;
            Assert.IsFalse(metadataProvider.TryGetBackingFieldToExposedFieldMap("InvalidEntity", out _), "Column to entity mappings should not exist for invalid entity.");
            Assert.IsFalse(metadataProvider.TryGetExposedFieldToBackingFieldMap("invalidEntity", out _), "Entity to column mappings should not exist for invalid entity.");
            Assert.IsTrue(metadataProvider.TryGetExposedFieldToBackingFieldMap("Publisher", out IReadOnlyDictionary<string, string> _), "Entity to column mappings should exist for valid entity.");
            Assert.IsTrue(metadataProvider.TryGetBackingFieldToExposedFieldMap("Publisher", out IReadOnlyDictionary<string, string> _), "Column to entity mappings should exist for valid entity.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow("/mygql", "/graphql", true, DisplayName = "Entity Rest path conflicts with default path /graphql")]
        [DataRow("/mygql", "/mygql", true, DisplayName = "Entity Rest path conflicts with configured GraphQL path")]
        [DataRow("/mygql", "mygql", true, DisplayName = "Entity Name mygql conflicts with configured GraphQL path")]
        [DataRow("/mygql", "graphql", true, DisplayName = "Entity Name graphql conflicts with default path /graphql")]
        [DataRow("/mygql", "", false, DisplayName = "Entity name does not conflict with GraphQL paths")]
        [DataRow("/mygql", "/entityRestPath", false, DisplayName = "Entity Rest path does not conflict with GraphQL paths")]
        [DataRow("/mygql", "entityName", false, DisplayName = "Entity name does not conflict with GraphQL paths")]
        public void TestEntityRESTPathDoesNotCollideWithGraphQLPaths(
            string graphQLConfigPath,
            string entityPath,
            bool expectsError)
        {
            try
            {
                DatabaseEngine = TestCategory.MSSQL;
                TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
                RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig();
                RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
                SetUpSQLMetadataProvider(runtimeConfigProvider);
                ((MsSqlMetadataProvider)_sqlMetadataProvider).ValidateEntityAndGraphQLPathUniqueness(path: entityPath, graphQLGlobalPath: graphQLConfigPath);
                if (expectsError)
                {
                    Assert.Fail(message: "REST and GraphQL path validation expected to fail.");
                }
            }
            catch (DataApiBuilderException ex)
            {
                if (expectsError)
                {
                    Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: ex.StatusCode);
                    Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: ex.SubStatusCode);
                }
                else
                {
                    Assert.Fail(message: "REST and GraphQL path validation expected to pass.");
                }
            }
        }

        /// <summary>
        /// Unit tests MsSqlMetadataProvider.IsGraphQLReservedName(entity, databaseColumnName)
        /// ensuring that the value for the databaseColumnName argument is not a GraphQL introspection system reserved name.
        /// If a violation is detected, identify whether the entity has a mapped value (alias) for the column name, and
        /// evaluate the mapped value against name restrictions. 
        /// </summary>
        /// <param name="dbColumnName">Database column name.</param>
        /// <param name="mappedName">Column name mapped value (alias), if configured.</param>
        /// <param name="expectsError">True/False</param>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow("__typename", null, true, DisplayName = "Database column name, no mapped value, that violates GraphQL name rules.")]
        [DataRow("__typename", "typeName", false, DisplayName = "Database column name (name violation) with rule conforming mapped value.")]
        [DataRow("__typename", "__typeName2", true, DisplayName = "Database column name and mapped value violate GraphQL name rules")]
        [DataRow("ColumnName", null, false, DisplayName = "Database column name, no mapped value, conforming to GraphQL name rules.")]
        [DataRow("ColumnName", "__columnName", true, DisplayName = "Database column with mapped value violating GraphQL name rules.")]
        public void ValidateGraphQLReservedNaming_DatabaseColumns(string dbColumnName, string mappedName, bool expectsError)
        {
            Dictionary<string, string> columnNameMappings = new();
            columnNameMappings.Add(key: dbColumnName, value: mappedName);

            Entity sampleEntity = new(
                Source: new("sampleElement", EntitySourceType.Table, null, null),
                Rest: new(Enabled: false),
                GraphQL: new("", ""),
                Permissions: new EntityPermission[] { ConfigurationTests.GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: columnNameMappings
                );

            bool actualIsNameViolation = MsSqlMetadataProvider.IsGraphQLReservedName(sampleEntity, dbColumnName, graphQLEnabledGlobally: true);
            Assert.AreEqual(
                expected: expectsError,
                actual: actualIsNameViolation,
                message: "Unexpected failure. fieldName: " + dbColumnName + " | fieldMapping:" + mappedName);

            bool isViolationWithGraphQLGloballyDisabled = MsSqlMetadataProvider.IsGraphQLReservedName(sampleEntity, dbColumnName, graphQLEnabledGlobally: false);
            Assert.AreEqual(
                expected: false,
                actual: isViolationWithGraphQLGloballyDisabled,
                message: "Unexpected failure. fieldName: " + dbColumnName + " | fieldMapping:" + mappedName);
        }
    }
}

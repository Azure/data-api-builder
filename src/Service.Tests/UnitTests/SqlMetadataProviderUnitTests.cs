// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Http;
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
            string dataSourceName = runtimeConfig.DefaultDataSourceName;

            ILogger<ISqlMetadataProvider> sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;
            Mock<IQueryExecutor> queryExecutor = new();
            IQueryBuilder queryBuilder = new MsSqlQueryBuilder();

            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(queryBuilder);
            queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(queryExecutor.Object);

            IFileSystem fileSystem = new FileSystem();
            ILogger<RuntimeConfigValidator> validatorLogger = new Mock<ILogger<RuntimeConfigValidator>>().Object;
            RuntimeConfigValidator runtimeConfigValidator = new(runtimeConfigProvider, fileSystem, validatorLogger);

            SqlMetadataProvider<SqlConnection, SqlDataAdapter, SqlCommand> provider = new MsSqlMetadataProvider(
                runtimeConfigProvider,
                runtimeConfigValidator,
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

            // For strings that are an invalid format for the connection string builder, need to
            // redirect std error to a string writer for comparison to expected error messaging later.
            StringWriter sw = new();
            Console.SetError(sw);

            DatabaseEngine = TestCategory.POSTGRESQL;
            await CheckExceptionForBadConnectionStringHelperAsync(DatabaseEngine, connectionString, sw);
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
            else if (string.Equals(databaseType, TestCategory.POSTGRESQL))
            {
                _queryBuilder = new PostgresQueryBuilder();
            }

            try
            {
                string dataSourceName = runtimeConfigProvider.GetConfig().DefaultDataSourceName;
                // Setup Mock query manager Factory
                Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
                queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(_queryBuilder);
                queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(_queryExecutor);

                IFileSystem fileSystem = new FileSystem();
                Mock<ILogger<RuntimeConfigValidator>> loggerValidator = new();
                RuntimeConfigValidator runtimeConfigValidator = new(runtimeConfigProvider, fileSystem, loggerValidator.Object);

                ISqlMetadataProvider sqlMetadataProvider = databaseType switch
                {
                    TestCategory.MSSQL => new MsSqlMetadataProvider(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory.Object, sqlMetadataLogger, dataSourceName),
                    TestCategory.MYSQL => new MySqlMetadataProvider(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory.Object, sqlMetadataLogger, dataSourceName),
                    TestCategory.POSTGRESQL => new PostgreSqlMetadataProvider(runtimeConfigProvider, runtimeConfigValidator, queryManagerFactory.Object, sqlMetadataLogger, dataSourceName),
                    _ => throw new ArgumentException($"Invalid database type: {databaseType}")
                };

                await sqlMetadataProvider.InitializeAsync();
            }
            catch (DataApiBuilderException ex)
            {
                // Combine both the console and exception messages because they both
                // may contain the connection string errors this function expects to exist.
                if (sw is not null)
                {
                    await TestHelper.DelayTask(() => string.IsNullOrWhiteSpace(sw.ToString()));
                }

                string consoleMessages = sw is not null ? sw.ToString() : string.Empty;
                string allErrorMessages = ex.Message + " " + consoleMessages;
                Assert.IsTrue(allErrorMessages.Contains(DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE),
                    $"Current message does not contain the expected connection string error message: {allErrorMessages}");
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
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
                Fields: null,
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

        /// <summary>
        /// Test to validate successful inference of relationship data based on data provided in the config and the metadata
        /// collected from the MsSql database.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateInferredRelationshipInfoForMsSql()
        {
            DatabaseEngine = TestCategory.MSSQL;
            await SetupTestFixtureAndInferMetadata();
            ValidateInferredRelationshipInfoForTables();
        }

        /// <summary>
        /// Test to validate successful inference of relationship data based on data provided in the config and the metadata
        /// collected from the MySql database.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MYSQL)]
        public async Task ValidateInferredRelationshipInfoForMySql()
        {
            DatabaseEngine = TestCategory.MYSQL;
            await SetupTestFixtureAndInferMetadata();
            ValidateInferredRelationshipInfoForTables();
        }

        /// <summary>
        /// Test to validate successful inference of relationship data based on data provided in the config and the metadata
        /// collected from the PgSql database.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.POSTGRESQL)]
        public async Task ValidateInferredRelationshipInfoForPgSql()
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await SetupTestFixtureAndInferMetadata();
            ValidateInferredRelationshipInfoForTables();
        }

        /// <summary>
        /// Data-driven test to validate that DataApiBuilderException is thrown for various invalid resultFieldName values
        /// during stored procedure result set definition population.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow(null, DisplayName = "Null result field name")]
        [DataRow("", DisplayName = "Empty result field name")]
        [DataRow("   ", DisplayName = "Multiple spaces result field name")]
        public async Task ValidateExceptionForInvalidResultFieldNames(string invalidFieldName)
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
            RuntimeConfig baseConfigFromDisk = SqlTestHelper.SetupRuntimeConfig();

            // Create a RuntimeEntities with ONLY our test stored procedure entity
            Dictionary<string, Entity> entitiesDictionary = new()
            {
                {
                    "get_book_by_id", new Entity(
                        Source: new("dbo.get_book_by_id", EntitySourceType.StoredProcedure, null, null),
                        Fields: null,
                        Rest: new(Enabled: true),
                        GraphQL: new("get_book_by_id", "get_book_by_ids", Enabled: true),
                        Permissions: new EntityPermission[] {
                            new(
                                Role: "anonymous",
                                Actions: new EntityAction[] {
                                    new(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                                })
                        },
                        Relationships: null,
                        Mappings: null
                    )
                }
            };

            RuntimeEntities entities = new(entitiesDictionary);
            RuntimeConfig runtimeConfig = baseConfigFromDisk with { Entities = entities };
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            ILogger<ISqlMetadataProvider> sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;

            // Setup query builder
            _queryBuilder = new MsSqlQueryBuilder();

            try
            {
                string dataSourceName = runtimeConfigProvider.GetConfig().DefaultDataSourceName;

                // Create mock query executor that always returns JsonArray with invalid field name
                Mock<IQueryExecutor> mockQueryExecutor = new();

                // Create a JsonArray that simulates the stored procedure result with invalid field name
                JsonArray invalidFieldJsonArray = new();
                JsonObject jsonObject = new()
                {
                    [BaseSqlQueryBuilder.STOREDPROC_COLUMN_NAME] = invalidFieldName, // This will be null, empty, or whitespace
                    [BaseSqlQueryBuilder.STOREDPROC_COLUMN_SYSTEMTYPENAME] = "varchar",
                    [BaseSqlQueryBuilder.STOREDPROC_COLUMN_ISNULLABLE] = false
                };
                invalidFieldJsonArray.Add(jsonObject);

                // Setup the mock to return our malformed JsonArray for all ExecuteQueryAsync calls
                mockQueryExecutor.Setup(x => x.ExecuteQueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, DbConnectionParam>>(),
                    It.IsAny<Func<DbDataReader, List<string>, Task<JsonArray>>>(),
                    It.IsAny<string>(),
                    It.IsAny<HttpContext>(),
                    It.IsAny<List<string>>()))
                    .ReturnsAsync(invalidFieldJsonArray);

                // Setup Mock query manager Factory
                Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
                queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(_queryBuilder);
                queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(mockQueryExecutor.Object);

                IFileSystem fileSystem = new FileSystem();
                Mock<ILogger<RuntimeConfigValidator>> loggerValidator = new();
                RuntimeConfigValidator runtimeConfigValidator = new(runtimeConfigProvider, fileSystem, loggerValidator.Object);

                ISqlMetadataProvider sqlMetadataProvider = new MsSqlMetadataProvider(
                    runtimeConfigProvider,
                    runtimeConfigValidator,
                    queryManagerFactory.Object,
                    sqlMetadataLogger,
                    dataSourceName);

                await sqlMetadataProvider.InitializeAsync();
                Assert.Fail($"Expected DataApiBuilderException was not thrown for invalid resultFieldName: '{invalidFieldName}'.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(ex.Message.Contains("returns a column without a name"));
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Helper method for test methods ValidateInferredRelationshipInfoFor{MsSql, MySql, and PgSql}.
        /// This helper validates that an entity's relationship data is correctly inferred based on config and database supplied relationship metadata.
        /// Each test verifies that the referencing entity is correctly determined based on the FK constraints in the database.
        /// </summary>
        private static void ValidateInferredRelationshipInfoForTables()
        {
            // Validate that when for an 1:N relationship between Book - Review, an FK constraint
            // exists from Review->Book.
            // DAB determines that Review is the referencing entity during startup.
            ValidateReferencingEntitiesForRelationship(
                sourceEntityName: "Book",
                targetEntityName: "Review",
                expectedReferencingEntityNames: new List<string>() { "Review" });

            // Validate that when for an 1:1 relationship between Stock - stocks_price, an FK constraint
            // exists from stocks_price -> Stock.
            // DAB determines that stocks_price is the referencing entity during startup.
            ValidateReferencingEntitiesForRelationship(
                sourceEntityName: "Stock",
                targetEntityName: "stocks_price",
                expectedReferencingEntityNames: new List<string>() { "stocks_price" });

            // Validate that when for an N:1 relationship between Book - Publisher, an FK constraint
            // exists from Book->Publisher.
            // DAB determiens that Book is the referencing entity during startup.
            ValidateReferencingEntitiesForRelationship(
                sourceEntityName: "Book",
                targetEntityName: "Publisher",
                expectedReferencingEntityNames: new List<string>() { "Book" });
        }

        /// <summary>
        /// Helper method to validate that for a given pair of source and target entities, DAB correctly infers the referencing entity/entities
        /// during startup.
        /// 1. For relationships backed by an FK, there is only one referencing entity.
        /// 2. For relationships not backed by an FK, there are two referencing entities because
        /// at startup, DAB can't determine which entity is the referencing entity. DAB can only determine the referecing entity
        /// during request execution.
        /// </summary>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="expectedReferencingEntityNames">List of expected referencing entity names.</param>
        private static void ValidateReferencingEntitiesForRelationship(
            string sourceEntityName,
            string targetEntityName,
            List<string> expectedReferencingEntityNames)
        {
            _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(sourceEntityName, out DatabaseObject sourceDbo);
            _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(targetEntityName, out DatabaseObject targetDbo);
            DatabaseTable sourceTable = (DatabaseTable)sourceDbo;
            DatabaseTable targetTable = (DatabaseTable)targetDbo;
            List<ForeignKeyDefinition> foreignKeys = sourceDbo.SourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];
            HashSet<DatabaseTable> expectedReferencingTables = new();
            HashSet<DatabaseTable> actualReferencingTables = new();
            foreach (string referencingEntityName in expectedReferencingEntityNames)
            {
                DatabaseTable referencingTable = referencingEntityName.Equals(sourceEntityName) ? sourceTable : targetTable;
                expectedReferencingTables.Add(referencingTable);
            }

            foreach (ForeignKeyDefinition foreignKey in foreignKeys)
            {
                if (foreignKey.ReferencedColumns.Count == 0)
                {
                    continue;
                }

                DatabaseTable actualReferencingTable = foreignKey.Pair.ReferencingDbTable;
                actualReferencingTables.Add(actualReferencingTable);
            }

            Assert.IsTrue(actualReferencingTables.SetEquals(expectedReferencingTables));
        }

        /// <summary>
        /// Resets the database state and infers metadata for all the entities exposed in the config.
        /// The `ResetDbStateAsync()` method executes the .sql script of the respective database type and
        /// serves as a setup phase for this test. 
        /// </summary>
        private static async Task SetupTestFixtureAndInferMetadata()
        {
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig();
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetUpSQLMetadataProvider(runtimeConfigProvider);
            await ResetDbStateAsync();
            await _sqlMetadataProvider.InitializeAsync();
        }

        /// <summary>
        /// Ensures that the query that returns the tables that will be generated
        /// into entities from the autoentities configuration returns the expected result.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow(new string[] { "dbo.%book%" }, new string[] { }, "{schema}.{object}.books", new string[] { "book" }, "")]
        [DataRow(new string[] { "dbo.%publish%" }, new string[] { }, "{schema}.{object}", new string[] { "publish" }, "")]
        [DataRow(new string[] { "dbo.%book%" }, new string[] { "dbo.%books%" }, "{schema}_{object}_exclude_books", new string[] { "book" }, "books")]
        [DataRow(new string[] { "dbo.%book%", "dbo.%publish%" }, new string[] { }, "{object}", new string[] { "book", "publish" }, "")]
        [DataRow(new string[] { }, new string[] { "dbo.%book%" }, "{object}s", new string[] { "" }, "book")]
        public async Task CheckAutoentitiesQuery(string[] include, string[] exclude, string name, string[] includeObject, string excludeObject)
        {
            // Arrange
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig();
            Autoentity autoentity = new(new AutoentityPatterns(include, exclude, name), null, null);
            Dictionary<string, Autoentity> dictAutoentity = new()
            {
                { "autoentity", autoentity }
            };
            RuntimeConfig configWithAutoentity = runtimeConfig with
            {
                Autoentities = new RuntimeAutoentities(dictAutoentity)
            };
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(configWithAutoentity);
            SetUpSQLMetadataProvider(runtimeConfigProvider);

            // Act
            MsSqlMetadataProvider metadataProvider = (MsSqlMetadataProvider)_sqlMetadataProvider;
            JsonArray resultArray = await metadataProvider.QueryAutoentitiesAsync("autoentity", autoentity);

            // Assert
            Assert.IsNotNull(resultArray);
            foreach (JsonObject resultObject in resultArray)
            {
                bool includedObjectExists = false;
                foreach (string included in includeObject)
                {
                    if (resultObject["object"].ToString().Contains(included))
                    {
                        includedObjectExists = true;
                        Assert.AreNotEqual(name, resultObject["entity_name"].ToString(), "Name returned by query should not include {schema} or {object}.");
                        if (include.Length > 0)
                        {
                            Assert.AreEqual(expected: "dbo", actual: resultObject["schema"].ToString(), "Query does not return expected schema.");
                        }

                        if (exclude.Length > 0)
                        {
                            Assert.IsTrue(!resultObject["object"].ToString().Contains(excludeObject), "Query returns pattern that should be excluded.");
                        }
                    }
                }

                Assert.IsTrue(includedObjectExists, "Query does not return expected object.");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        #region Embed Type Override

        // ─────────────────────────────────────────────────────────────────
        // Tests for MsSqlMetadataProvider.ApplyEmbedTypeOverride (Phase 3)
        //
        // The helper is invoked at startup for each parameter defined in
        // config for a stored procedure. When embed:true is set, it:
        //   - validates the parameter is VECTOR-shaped (SystemType == byte[])
        //   - overrides SystemType/DbType/SqlDbType to flow as String
        //
        // These tests construct ParameterDefinition / ParameterMetadata
        // pairs directly and invoke the helper — no DB, no DI container,
        // no full metadata-provider construction.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// embed:true on a parameter that SQL Server reports as Byte[] (i.e., VECTOR(N)
        /// surfaced via INFORMATION_SCHEMA.PARAMETERS as varbinary) overrides the
        /// metadata type so the vector JSON string flows through DAB's String pipeline.
        /// </summary>
        [TestMethod]
        public void ApplyEmbedTypeOverride_ByteArrayParam_WithEmbedTrue_OverridesToString()
        {
            ParameterDefinition def = new()
            {
                Name = "query_vector",
                SystemType = typeof(byte[])
            };
            ParameterMetadata meta = new() { Name = "query_vector", Embed = true };

            MsSqlMetadataProvider.ApplyEmbedTypeOverride(
                def, meta, schemaName: "dbo", storedProcedureName: "sp_search", parameterName: "query_vector");

            Assert.AreEqual(typeof(string), def.SystemType);
            Assert.AreEqual(System.Data.DbType.String, def.DbType);
            Assert.AreEqual(System.Data.SqlDbType.NVarChar, def.SqlDbType);
        }

        /// <summary>
        /// embed:true on a parameter that the SQL Server reports as something other than
        /// Byte[] — e.g., a real string param — must throw at startup with a 503 and
        /// a clear message naming the schema/sproc/parameter and saying VECTOR(N) is required.
        /// </summary>
        [TestMethod]
        public void ApplyEmbedTypeOverride_StringParam_WithEmbedTrue_ThrowsAtStartup()
        {
            ParameterDefinition def = new()
            {
                Name = "query_text",
                SystemType = typeof(string)
            };
            ParameterMetadata meta = new() { Name = "query_text", Embed = true };

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                MsSqlMetadataProvider.ApplyEmbedTypeOverride(
                    def, meta, schemaName: "dbo", storedProcedureName: "sp_test", parameterName: "query_text"));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            StringAssert.Contains(ex.Message, "dbo.sp_test");
            StringAssert.Contains(ex.Message, "query_text");
            StringAssert.Contains(ex.Message, "String");
            StringAssert.Contains(ex.Message, "VECTOR");
        }

        /// <summary>
        /// embed:true on a numeric parameter (e.g., INT) is also rejected with the same
        /// 503. Catches a common misconfiguration where embed is applied to a count param.
        /// </summary>
        [TestMethod]
        public void ApplyEmbedTypeOverride_IntParam_WithEmbedTrue_ThrowsAtStartup()
        {
            ParameterDefinition def = new()
            {
                Name = "top_k",
                SystemType = typeof(int)
            };
            ParameterMetadata meta = new() { Name = "top_k", Embed = true };

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                MsSqlMetadataProvider.ApplyEmbedTypeOverride(
                    def, meta, schemaName: "dbo", storedProcedureName: "sp_top", parameterName: "top_k"));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            StringAssert.Contains(ex.Message, "Int32");
            StringAssert.Contains(ex.Message, "VECTOR");
        }

        /// <summary>
        /// embed:false on a Byte[] parameter (e.g., a real varbinary blob like an image)
        /// must NOT be touched — the override only fires for embed:true params. The
        /// parameter's metadata should be returned exactly as it came in.
        /// </summary>
        [TestMethod]
        public void ApplyEmbedTypeOverride_ByteArrayParam_WithEmbedFalse_NoChange()
        {
            ParameterDefinition def = new()
            {
                Name = "image_blob",
                SystemType = typeof(byte[]),
                DbType = System.Data.DbType.Binary,
                SqlDbType = System.Data.SqlDbType.VarBinary
            };
            ParameterMetadata meta = new() { Name = "image_blob", Embed = false };

            MsSqlMetadataProvider.ApplyEmbedTypeOverride(
                def, meta, schemaName: "dbo", storedProcedureName: "sp_upload", parameterName: "image_blob");

            // Original type metadata preserved exactly
            Assert.AreEqual(typeof(byte[]), def.SystemType);
            Assert.AreEqual(System.Data.DbType.Binary, def.DbType);
            Assert.AreEqual(System.Data.SqlDbType.VarBinary, def.SqlDbType);
        }

        /// <summary>
        /// Edge case: embed:true behavior is independent of <see cref="ParameterDefinition.Required"/>
        /// and <see cref="ParameterDefinition.Default"/>. The override fires purely on
        /// <see cref="ParameterMetadata.Embed"/> + <see cref="Type"/> being byte[]. Other
        /// metadata fields are not affected.
        /// </summary>
        [TestMethod]
        public void ApplyEmbedTypeOverride_ByteArrayWithRequiredAndDefault_OnlyTypeMetadataChanges()
        {
            ParameterDefinition def = new()
            {
                Name = "v",
                SystemType = typeof(byte[]),
                Required = true,
                Default = "should-be-untouched-by-override",
                HasConfigDefault = true,
                ConfigDefaultValue = "should-be-untouched-by-override"
            };
            ParameterMetadata meta = new() { Name = "v", Embed = true };

            MsSqlMetadataProvider.ApplyEmbedTypeOverride(
                def, meta, schemaName: "dbo", storedProcedureName: "sp_v", parameterName: "v");

            // Type metadata overridden
            Assert.AreEqual(typeof(string), def.SystemType);
            Assert.AreEqual(System.Data.DbType.String, def.DbType);
            Assert.AreEqual(System.Data.SqlDbType.NVarChar, def.SqlDbType);

            // Other fields untouched (note: validator separately rejects embed:true + default
            // before request execution; this test confirms the metadata helper itself is
            // narrow-scoped and doesn't mutate fields outside its concern).
            Assert.AreEqual(true, def.Required);
            Assert.AreEqual("should-be-untouched-by-override", def.Default);
            Assert.AreEqual(true, def.HasConfigDefault);
            Assert.AreEqual("should-be-untouched-by-override", def.ConfigDefaultValue);
        }

        /// <summary>
        /// Multi-parameter scenario: in a real metadata-loading loop, the helper is
        /// called once per parameter. Calling it on a mix of embed:true Byte[] params
        /// (overridden) and embed:false params (untouched) confirms the helper has no
        /// cross-call state.
        /// </summary>
        [TestMethod]
        public void ApplyEmbedTypeOverride_MultipleParams_OnlyEmbedTrueOnesOverridden()
        {
            ParameterDefinition embedDef = new()
            {
                Name = "primary_query",
                SystemType = typeof(byte[])
            };
            ParameterDefinition normalDef = new()
            {
                Name = "top_k",
                SystemType = typeof(int)
            };
            ParameterDefinition anotherEmbedDef = new()
            {
                Name = "secondary_query",
                SystemType = typeof(byte[])
            };

            MsSqlMetadataProvider.ApplyEmbedTypeOverride(
                embedDef,
                new ParameterMetadata { Name = "primary_query", Embed = true },
                schemaName: "dbo", storedProcedureName: "sp_hybrid", parameterName: "primary_query");
            MsSqlMetadataProvider.ApplyEmbedTypeOverride(
                normalDef,
                new ParameterMetadata { Name = "top_k", Embed = false },
                schemaName: "dbo", storedProcedureName: "sp_hybrid", parameterName: "top_k");
            MsSqlMetadataProvider.ApplyEmbedTypeOverride(
                anotherEmbedDef,
                new ParameterMetadata { Name = "secondary_query", Embed = true },
                schemaName: "dbo", storedProcedureName: "sp_hybrid", parameterName: "secondary_query");

            // Both embed:true params overridden to String pipeline
            Assert.AreEqual(typeof(string), embedDef.SystemType);
            Assert.AreEqual(typeof(string), anotherEmbedDef.SystemType);

            // Non-embed param's type unchanged
            Assert.AreEqual(typeof(int), normalDef.SystemType);
        }

        #endregion
    }
}

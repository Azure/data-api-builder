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
        /// Test to validate that a table holding a column whose data type the data provider cannot
        /// map to a CLR type - here a geometry column - is still usable: metadata inference must
        /// succeed and the unsupported column must be absent from the inferred source definition,
        /// so it never reaches the OData or GraphQL type maps.
        /// The entity places no field restriction, so this covers the column being skipped on the
        /// strength of its type alone.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateUnsupportedColumnTypeIsNotInferred()
        {
            DatabaseEngine = TestCategory.MSSQL;
            await SetupTestFixtureAndInferMetadata();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue("GeometryType", out DatabaseObject databaseObject),
                message: "Metadata inference failed for the entity backed by a table with a geometry column.");

            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;

            Assert.IsTrue(
                sourceDefinition.Columns.ContainsKey("id"),
                message: "The primary key column is expected in the source definition.");
            Assert.IsTrue(
                sourceDefinition.Columns.ContainsKey("name"),
                message: "A column with a supported data type is expected in the source definition.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("geom"),
                message: "A column whose data type cannot be mapped is not expected in the source definition.");

            // Identity is carried from the catalog on the narrowed path rather than through
            // DataColumn.AutoIncrement. Asserted here because losing it would silently make creates
            // require an identity value.
            Assert.IsTrue(
                sourceDefinition.Columns["id"].IsAutoGenerated,
                message: "The identity column is expected to be marked auto-generated.");
            Assert.IsTrue(
                sourceDefinition.Columns["id"].IsReadOnly,
                message: "An auto-generated column is expected to be read-only.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that an identity column whose CLR type cannot be auto-incremented keeps
        /// the type the provider reported.
        /// `DataColumn.AutoIncrement` coerces such a DataType to Int32, and SQL Server allows
        /// identity on tinyint, numeric and decimal, so carrying the flag through that property
        /// would report the wrong SystemType and reach parameter typing and the generated API
        /// schemas. This is the fixture that proves the type survives.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateIdentityTypeIsPreservedOnTheNarrowedPath()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "DecimalIdentityGeometry",
                BuildReadOnlyEntity(
                    entityName: "DecimalIdentityGeometry",
                    databaseObject: "dbo.decimal_identity_geometry_table",
                    sourceType: EntitySourceType.Table,
                    keyFields: null));

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue("DecimalIdentityGeometry", out DatabaseObject databaseObject),
                message: "Metadata inference failed for an object with a decimal identity column.");

            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;

            Assert.AreEqual(
                typeof(decimal),
                sourceDefinition.Columns["id"].SystemType,
                message: "The identity column is expected to keep the data type the provider reported.");
            Assert.IsTrue(
                sourceDefinition.Columns["id"].IsAutoGenerated,
                message: "The identity column is expected to be marked auto-generated.");
            Assert.IsTrue(
                sourceDefinition.Columns["id"].IsReadOnly,
                message: "An auto-generated column is expected to be read-only.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("geom"),
                message: "A column whose data type cannot be mapped is not expected in the source definition.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a database policy referencing a column left out of the projection
        /// fails initialization.
        /// A policy is parsed per request against the OData model, which is built from
        /// `SourceDefinition.Columns`, so a policy naming an absent column returns 400 on every
        /// request for that role instead of the configuration being rejected once, at startup.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateDatabasePolicyOverUnsupportedColumnFailsInitialization()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            Entity entity = new(
                Source: new("dbo.geometry_type_table", EntitySourceType.Table, null, new string[] { "id" }),
                Fields: null,
                Rest: new(Enabled: true),
                GraphQL: new("GeometryPolicy", "GeometryPolicys", Enabled: true),
                Permissions: new EntityPermission[]
                {
                    new(Role: "anonymous",
                        Actions: new EntityAction[]
                        {
                            new(Action: EntityActionOperation.Read,
                                Fields: null,
                                Policy: new(Request: null, Database: "@item.geom eq null"))
                        })
                },
                Relationships: null,
                Mappings: null);

            await SetUpSingleEntityMetadataProviderAsync("GeometryPolicy", entity);

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a database policy over a column of an unsupported data type.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(
                    ex.Message.Contains("geom") && ex.Message.Contains("policy"),
                    message: $"The error is expected to name the column and the policy referencing it. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a database object whose every column has a data type the data
        /// provider cannot map fails initialization with a specific error, rather than falling back
        /// to reading every column and surfacing the provider's opaque failure instead of the reason.
        /// The entity is declared in an in-memory config rather than in dab-config.MsSql.json,
        /// because this object fails by design and every MSSQL fixture initializes every configured
        /// entity.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateObjectWithOnlyUnsupportedColumnsFailsInitialization()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            Dictionary<string, Entity> entities = new()
            {
                {
                    "GeometryOnlyView",
                    new Entity(
                        Source: new("dbo.geometry_only_view", EntitySourceType.View, null, new string[] { "geom" }),
                        Fields: null,
                        Rest: new(Enabled: true),
                        GraphQL: new("GeometryOnlyView", "GeometryOnlyViews", Enabled: true),
                        Permissions: new EntityPermission[]
                        {
                            new(Role: "anonymous",
                                Actions: new EntityAction[] { new(Action: EntityActionOperation.Read, Fields: null, Policy: null) })
                        },
                        Relationships: null,
                        Mappings: null)
                }
            };

            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig() with { Entities = new RuntimeEntities(entities) };
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetUpSQLMetadataProvider(runtimeConfigProvider);
            await ResetDbStateAsync();

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for an object whose every column has an unsupported data type.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(
                    ex.Message.Contains("has a data type that is not supported"),
                    message: $"Unexpected exception message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a configured primary key naming a column of an unsupported data
        /// type fails initialization instead of producing a source definition whose primary key is
        /// absent from its columns. The error names both the column and the offending type.
        /// The entity is declared in an in-memory config for the same reason as the test above.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidatePrimaryKeyOnUnsupportedColumnFailsInitialization()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            Dictionary<string, Entity> entities = new()
            {
                {
                    "GeometryKeyed",
                    new Entity(
                        Source: new("dbo.geometry_type_table", EntitySourceType.Table, null, new string[] { "geom" }),
                        Fields: null,
                        Rest: new(Enabled: true),
                        GraphQL: new("GeometryKeyed", "GeometryKeyeds", Enabled: true),
                        Permissions: new EntityPermission[]
                        {
                            new(Role: "anonymous",
                                Actions: new EntityAction[] { new(Action: EntityActionOperation.Read, Fields: null, Policy: null) })
                        },
                        Relationships: null,
                        Mappings: null)
                }
            };

            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig() with { Entities = new RuntimeEntities(entities) };
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetUpSQLMetadataProvider(runtimeConfigProvider);
            await ResetDbStateAsync();

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a primary key of an unsupported data type.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(
                    ex.Message.Contains("geom") && ex.Message.Contains("geometry"),
                    message: $"The error is expected to name the column and its data type. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that the period columns of a temporal table declared
        /// GENERATED ALWAYS ... HIDDEN stay out of the inferred source definition.
        /// "SELECT *" does not return them, so an explicit projection built from the catalog must not
        /// name them either: adding them widens the exposed contract, and the read-only
        /// classification does not recognize generated-always period columns, so an overwriting PUT
        /// would try to null them and fail.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateHiddenPeriodColumnsAreNotInferred()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "TemporalGeometryType",
                BuildReadOnlyEntity(
                    entityName: "TemporalGeometryType",
                    databaseObject: "dbo.temporal_geometry_type_table",
                    sourceType: EntitySourceType.Table,
                    keyFields: new string[] { "id" }));

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue("TemporalGeometryType", out DatabaseObject databaseObject),
                message: "Metadata inference failed for the entity backed by a temporal table.");

            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;

            Assert.IsTrue(
                sourceDefinition.Columns.ContainsKey("id"),
                message: "The configured key column is expected in the source definition.");
            Assert.IsTrue(
                sourceDefinition.Columns.ContainsKey("name"),
                message: "A column with a supported data type is expected in the source definition.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("geom"),
                message: "A column whose data type cannot be mapped is not expected in the source definition.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("valid_from"),
                message: "A HIDDEN period column is not returned by SELECT * and is not expected in the source definition.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("valid_to"),
                message: "A HIDDEN period column is not returned by SELECT * and is not expected in the source definition.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that an object whose own database primary key is a column of an
        /// unsupported data type fails initialization with the reason.
        /// The projection cannot carry that column, and the engine cannot operate on the object
        /// without its key, so the object is unreachable either way. What this asserts is that the
        /// failure names the column and its type instead of reporting a missing primary key, which
        /// reads as something the user forgot to configure.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateUnsupportedDatabasePrimaryKeyFailsInitialization()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "HierarchyIdKeyed",
                BuildReadOnlyEntity(
                    entityName: "HierarchyIdKeyed",
                    databaseObject: "dbo.hierarchyid_pk_table",
                    sourceType: EntitySourceType.Table,
                    keyFields: null));

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a database primary key of an unsupported data type.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(
                    ex.Message.Contains("node") && ex.Message.Contains("hierarchyid"),
                    message: $"The error is expected to name the key column and its data type. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate the same rejection when the unsupported column is one member of a
        /// composite database primary key. The supported member alone does not identify a row, so
        /// the object cannot be exposed through a partial key.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateUnsupportedColumnInCompositeDatabasePrimaryKeyFailsInitialization()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "HierarchyIdCompositeKeyed",
                BuildReadOnlyEntity(
                    entityName: "HierarchyIdCompositeKeyed",
                    databaseObject: "dbo.hierarchyid_composite_pk_table",
                    sourceType: EntitySourceType.Table,
                    keyFields: null));

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a composite database primary key holding an unsupported data type.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(
                    ex.Message.Contains("node") && ex.Message.Contains("hierarchyid"),
                    message: $"The error is expected to name the key column and its data type. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that an object carrying a unique index over a column of an unsupported
        /// data type loads when a supported key is configured through source.key-fields.
        /// This is the case the data adapter cannot serve: FillSchema runs with
        /// CommandBehavior.KeyInfo, under which the provider performs its own key discovery and
        /// appends key columns missing from the SELECT list as hidden reader columns - so the
        /// unsupported unique column comes back regardless of the projection, and configuring a
        /// supported key does not change that. Schema discovery therefore reads the shape without
        /// KeyInfo once the projection is narrowed, and takes the primary key from the catalog.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateConfiguredKeyIsUsedWhenUniqueIndexColumnIsUnsupported()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "HierarchyIdUnique",
                BuildReadOnlyEntity(
                    entityName: "HierarchyIdUnique",
                    databaseObject: "dbo.hierarchyid_unique_table",
                    sourceType: EntitySourceType.Table,
                    keyFields: new string[] { "id" }));

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue("HierarchyIdUnique", out DatabaseObject databaseObject),
                message: "Metadata inference failed for an object whose unique index covers an unsupported column.");

            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;

            Assert.IsTrue(
                sourceDefinition.Columns.ContainsKey("id"),
                message: "The configured key column is expected in the source definition.");
            Assert.IsTrue(
                sourceDefinition.Columns.ContainsKey("name"),
                message: "A column with a supported data type is expected in the source definition.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("node"),
                message: "The unsupported unique column is not expected in the source definition.");
            CollectionAssert.AreEqual(
                new List<string> { "id" },
                sourceDefinition.PrimaryKey,
                message: "The configured key is expected to be the primary key in effect.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that configuration naming a column left out of the projection fails
        /// initialization.
        /// Such a name keeps resolving after the column is gone, because the exposed and backing
        /// column maps are built from entity fields and mappings without requiring the column to
        /// exist in the source definition. The reference then reaches code that indexes the source
        /// definition columns and fails per request rather than at startup.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateConfiguredReferenceToUnsupportedColumnFailsInitialization()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "GeometryAliased",
                BuildReadOnlyEntity(
                    entityName: "GeometryAliased",
                    databaseObject: "dbo.geometry_type_table",
                    sourceType: EntitySourceType.Table,
                    keyFields: new string[] { "id" },
                    mappings: new Dictionary<string, string> { { "geom", "Position" } }));

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a mapping over a column of an unsupported data type.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(
                    ex.Message.Contains("geom") && ex.Message.Contains("mappings"),
                    message: $"The error is expected to name the column and the configuration section referencing it. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that an object with no database primary key, whose unique index covers a
        /// supported non-null column, has that key inferred without `source.key-fields`.
        /// `DbDataAdapter.FillSchema` promotes such a unique key to `DataTable.PrimaryKey` on the
        /// unnarrowed path, so dropping it once the projection is narrowed would make the two paths
        /// disagree and demand configuration for an object the other path resolves on its own.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateUniqueKeyIsInferredWhenNoDatabasePrimaryKeyExists()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "UniqueKeyGeometry",
                BuildReadOnlyEntity(
                    entityName: "UniqueKeyGeometry",
                    databaseObject: "dbo.unique_key_geometry_table",
                    sourceType: EntitySourceType.Table,
                    keyFields: null));

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue("UniqueKeyGeometry", out DatabaseObject databaseObject),
                message: "Metadata inference failed for an object whose only key is a unique index over a supported column.");

            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;

            CollectionAssert.AreEqual(
                new List<string> { "code" },
                sourceDefinition.PrimaryKey,
                message: "The non-null unique column is expected to be inferred as the primary key.");
            Assert.IsTrue(
                sourceDefinition.Columns.ContainsKey("name"),
                message: "A column with a supported data type is expected in the source definition.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("geom"),
                message: "A column whose data type cannot be mapped is not expected in the source definition.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a database policy whose text merely contains "@item." inside a
        /// string literal is not read as a field reference.
        /// The policy below compares a literal to itself, so the two occurrences of "@item.geom" are
        /// data, not column references, and the entity has to start.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateQuotedLiteralInDatabasePolicyIsNotAFieldReference()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            Entity entity = new(
                Source: new("dbo.geometry_type_table", EntitySourceType.Table, null, new string[] { "id" }),
                Fields: null,
                Rest: new(Enabled: true),
                GraphQL: new("GeometryQuotedPolicy", "GeometryQuotedPolicys", Enabled: true),
                Permissions: new EntityPermission[]
                {
                    new(Role: "anonymous",
                        Actions: new EntityAction[]
                        {
                            new(Action: EntityActionOperation.Read,
                                Fields: null,
                                Policy: new(Request: null, Database: "@item.id gt 0 and '@item.geom' eq '@item.geom'"))
                        })
                },
                Relationships: null,
                Mappings: null);

            await SetUpSingleEntityMetadataProviderAsync("GeometryQuotedPolicy", entity);

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().ContainsKey("GeometryQuotedPolicy"),
                message: "A policy containing the field prefix only inside a string literal must not be read as a reference.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a policy referencing an alias of a supported column is honored even
        /// when the alias carries the name of a skipped column.
        /// Policy identifiers are exposed names, so "geom" here is the alias of the supported "name"
        /// column and not the skipped spatial column that happens to share the name.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateDatabasePolicyOverAliasOfSupportedColumnIsHonored()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            Entity entity = new(
                Source: new("dbo.geometry_type_table", EntitySourceType.Table, null, new string[] { "id" }),
                Fields: new List<FieldMetadata> { new() { Name = "name", Alias = "geom" } },
                Rest: new(Enabled: true),
                GraphQL: new("GeometryAliasPolicy", "GeometryAliasPolicys", Enabled: true),
                Permissions: new EntityPermission[]
                {
                    new(Role: "anonymous",
                        Actions: new EntityAction[]
                        {
                            new(Action: EntityActionOperation.Read,
                                Fields: null,
                                Policy: new(Request: null, Database: "@item.geom ne null"))
                        })
                },
                Relationships: null,
                Mappings: null);

            await SetUpSingleEntityMetadataProviderAsync("GeometryAliasPolicy", entity);

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().ContainsKey("GeometryAliasPolicy"),
                message: "A policy referencing an alias of a supported column must be honored, whatever the alias is named.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a simple view over a table holding an unsupported column keeps the
        /// key inference it had before the projection was narrowed.
        /// A view has no index of its own, so the catalog holds no key for it; the data adapter
        /// resolved one through the underlying table, and describing the projection recovers the
        /// same information without reintroducing the columns the projection left out.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateViewKeyIsInferredWithoutConfiguredKeyFields()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "GeometryAllView",
                BuildReadOnlyEntity(
                    entityName: "GeometryAllView",
                    databaseObject: "dbo.geometry_all_view",
                    sourceType: EntitySourceType.View,
                    keyFields: null));

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue("GeometryAllView", out DatabaseObject databaseObject),
                message: "Metadata inference failed for a simple view with no configured key.");

            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;

            CollectionAssert.AreEqual(
                new List<string> { "id" },
                sourceDefinition.PrimaryKey,
                message: "The key of the underlying table is expected to be inferred for the view.");
            Assert.IsTrue(
                sourceDefinition.Columns.ContainsKey("name"),
                message: "A column with a supported data type is expected in the source definition.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("geom"),
                message: "A column whose data type cannot be mapped is not expected in the source definition.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a view whose underlying key has a member the projection cannot
        /// express does not load with a partial key.
        /// Reporting only the readable member would produce a key that does not identify a row, and
        /// a partial key silently matches more than one row on an update or a delete — worse than
        /// the failure it would replace.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateViewOverPartiallyUnsupportedKeyDoesNotInferPartialKey()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "HierarchyIdCompositeView",
                BuildReadOnlyEntity(
                    entityName: "HierarchyIdCompositeView",
                    databaseObject: "dbo.hierarchyid_composite_view",
                    sourceType: EntitySourceType.View,
                    keyFields: null));

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a view whose underlying key cannot be expressed by the projection.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(
                    ex.Message.Contains("node"),
                    message: "The error is expected to name the key member the object does not expose, rather than "
                        + $"report a missing primary key. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a view which does not select the underlying primary key is still
        /// keyed through a unique key it does carry whole.
        /// Unlike a table, where a key column absent from the projection means it was dropped as
        /// unsupported, a view may simply not select the primary key. That is ordinary design, not a
        /// key the object cannot express, so the search continues into the unique keys.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateViewWithoutPrimaryKeyFallsBackToUniqueKey()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "UniqueWithoutPkView",
                BuildReadOnlyEntity(
                    entityName: "UniqueWithoutPkView",
                    databaseObject: "dbo.unique_without_pk_view",
                    sourceType: EntitySourceType.View,
                    keyFields: null));

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue("UniqueWithoutPkView", out DatabaseObject databaseObject),
                message: "Metadata inference failed for a view that carries a unique key but not the primary key.");

            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;

            CollectionAssert.AreEqual(
                new List<string> { "code" },
                sourceDefinition.PrimaryKey,
                message: "The unique key the view carries whole is expected to be the key in effect.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("geom"),
                message: "A column whose data type cannot be mapped is not expected in the source definition.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a view built on a join does not have one side's key inferred as its
        /// own, even when every column the projection selects resolves to that side.
        /// The second object of this view contributes only the unsupported column, so once that
        /// column is dropped nothing the projection selects points at it. Browse mode still reports
        /// its key column, flagged hidden, which is what identifies the result as a join. A key taken
        /// from the first object alone would not identify a row, because the join can duplicate its
        /// rows — the same class of defect as a partial key.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateViewOverJoinDoesNotInferKeyFromOneSide()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "JoinGeometryView",
                BuildReadOnlyEntity(
                    entityName: "JoinGeometryView",
                    databaseObject: "dbo.join_geometry_view",
                    sourceType: EntitySourceType.View,
                    keyFields: null));

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a view built on a join with no configured key.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
                Assert.IsTrue(
                    ex.Message.Contains("Primary key not configured"),
                    message: "A join is out of scope for key inference, so the object is expected to require "
                        + $"source.key-fields exactly as it did before. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a configured key loads a view whose underlying key the projection
        /// cannot express.
        /// The schema read runs for every object, so it also finds that no key of the object
        /// underneath is reachable here. That finding must not reject an object whose
        /// source.key-fields already says how to reach it — failing then would recommend precisely
        /// what was configured.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateConfiguredKeyIsHonoredWhenViewOmitsUnderlyingKey()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            await SetUpSingleEntityMetadataProviderAsync(
                "HierarchyIdCompositeViewWithKey",
                BuildReadOnlyEntity(
                    entityName: "HierarchyIdCompositeViewWithKey",
                    databaseObject: "dbo.hierarchyid_composite_view",
                    sourceType: EntitySourceType.View,
                    keyFields: new[] { "tenant_id" }));

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue("HierarchyIdCompositeViewWithKey", out DatabaseObject databaseObject),
                message: "Metadata inference failed for a view whose key is configured through source.key-fields.");

            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;

            CollectionAssert.AreEqual(
                new List<string> { "tenant_id" },
                sourceDefinition.PrimaryKey,
                message: "The configured key is expected to be the key in effect.");
            Assert.IsFalse(
                sourceDefinition.Columns.ContainsKey("node"),
                message: "A column whose data type cannot be mapped is not expected in the source definition.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a doubled quote inside a policy literal is read as an escaped quote
        /// and does not end the literal, so the field prefix that follows stays data.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateEscapedQuoteInDatabasePolicyLiteralIsNotAFieldReference()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            Entity entity = new(
                Source: new("dbo.geometry_type_table", EntitySourceType.Table, null, new string[] { "id" }),
                Fields: null,
                Rest: new(Enabled: true),
                GraphQL: new("GeometryEscapedPolicy", "GeometryEscapedPolicys", Enabled: true),
                Permissions: new EntityPermission[]
                {
                    new(Role: "anonymous",
                        Actions: new EntityAction[]
                        {
                            new(Action: EntityActionOperation.Read,
                                Fields: null,
                                Policy: new(Request: null, Database: "@item.name ne 'it''s @item.geom'"))
                        })
                },
                Relationships: null,
                Mappings: null);

            await SetUpSingleEntityMetadataProviderAsync("GeometryEscapedPolicy", entity);

            await _sqlMetadataProvider.InitializeAsync();

            Assert.IsTrue(
                _sqlMetadataProvider.GetEntityNamesAndDbObjects().ContainsKey("GeometryEscapedPolicy"),
                message: "A doubled quote inside a literal must not end it, so the prefix that follows is not a reference.");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that the "fields" alias format is rejected when it names a column left
        /// out of the projection, the same way "mappings" is.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateFieldsAliasOverUnsupportedColumnFailsInitialization()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            Entity entity = new(
                Source: new("dbo.geometry_type_table", EntitySourceType.Table, null, new string[] { "id" }),
                Fields: new List<FieldMetadata> { new() { Name = "geom", Alias = "Position" } },
                Rest: new(Enabled: true),
                GraphQL: new("GeometryFieldsAlias", "GeometryFieldsAliass", Enabled: true),
                Permissions: new EntityPermission[]
                {
                    new(Role: "anonymous",
                        Actions: new EntityAction[] { new(Action: EntityActionOperation.Read, Fields: null, Policy: null) })
                },
                Relationships: null,
                Mappings: null);

            await SetUpSingleEntityMetadataProviderAsync("GeometryFieldsAlias", entity);

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a fields alias over a column of an unsupported data type.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(
                    ex.Message.Contains("geom") && ex.Message.Contains("fields"),
                    message: $"The error is expected to name the column and the configuration section. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Test to validate that a permission's "fields.include" naming a column left out of the
        /// projection fails initialization.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task ValidateIncludedFieldOverUnsupportedColumnFailsInitialization()
        {
            DatabaseEngine = TestCategory.MSSQL;
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);

            Entity entity = new(
                Source: new("dbo.geometry_type_table", EntitySourceType.Table, null, new string[] { "id" }),
                Fields: null,
                Rest: new(Enabled: true),
                GraphQL: new("GeometryIncluded", "GeometryIncludeds", Enabled: true),
                Permissions: new EntityPermission[]
                {
                    new(Role: "anonymous",
                        Actions: new EntityAction[]
                        {
                            new(Action: EntityActionOperation.Read,
                                Fields: new EntityActionFields(
                                    Exclude: new HashSet<string>(),
                                    Include: new HashSet<string> { "id", "name", "geom" }),
                                Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null);

            await SetUpSingleEntityMetadataProviderAsync("GeometryIncluded", entity);

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
                Assert.Fail("Expected DataApiBuilderException was not thrown for a fields.include naming a column of an unsupported data type.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(
                    ex.Message.Contains("geom") && ex.Message.Contains("permissions"),
                    message: $"The error is expected to name the column and the configuration section. Actual message: {ex.Message}");
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Builds a metadata provider over a single in-memory entity and resets the database state.
        /// The objects exercised by the unsupported-data-type tests are declared in memory rather
        /// than in dab-config.MsSql.json, because several of them fail by design and every MSSQL
        /// fixture initializes every configured entity.
        /// </summary>
        private static async Task SetUpSingleEntityMetadataProviderAsync(string entityName, Entity entity)
        {
            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig()
                with
            { Entities = new RuntimeEntities(new Dictionary<string, Entity> { { entityName, entity } }) };
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            SetUpSQLMetadataProvider(runtimeConfigProvider);
            await ResetDbStateAsync();
        }

        /// <summary>
        /// Builds a read-only entity over a database object, with optional configured key fields and
        /// mappings.
        /// </summary>
        private static Entity BuildReadOnlyEntity(
            string entityName,
            string databaseObject,
            EntitySourceType sourceType,
            string[] keyFields,
            Dictionary<string, string> mappings = null)
        {
            return new Entity(
                Source: new(databaseObject, sourceType, null, keyFields),
                Fields: null,
                Rest: new(Enabled: true),
                GraphQL: new(entityName, $"{entityName}s", Enabled: true),
                Permissions: new EntityPermission[]
                {
                    new(Role: "anonymous",
                        Actions: new EntityAction[] { new(Action: EntityActionOperation.Read, Fields: null, Policy: null) })
                },
                Relationships: null,
                Mappings: mappings);
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
    }
}

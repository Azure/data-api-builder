using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
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
            _runtimeConfig = SqlTestHelper.SetupRuntimeConfig(DatabaseEngine);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(_runtimeConfig);
            SetUpSQLMetadataProvider();
            await ResetDbStateAsync();
            await _sqlMetadataProvider.InitializeAsync();
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set connection string and db type
        /// according to data row.
        /// <code>Check: </code>  Verify malformed connection string throws correct exception with MSSQL as the database.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow(";;;;;fooBarBAZ")]
        [DataRow("!&^%*&$$%#$%@$%#@()")]
        [DataRow("Server=<>;Databases=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;")]
        [DataRow("Servers=<>;Database=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;")]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests")]
        [DataRow("")]
        public async Task CheckExceptionForBadConnectionStringForMsSql(string connectionString)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await CheckExceptionForBadConnectionStringHelperAsync(DatabaseEngine, connectionString);
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
        private static async Task CheckExceptionForBadConnectionStringHelperAsync(string databaseType, string connectionString)
        {
            _runtimeConfig = SqlTestHelper.SetupRuntimeConfig(databaseType);
            _runtimeConfig.ConnectionString = connectionString;
            _sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);

            switch (databaseType)
            {
                case TestCategory.MSSQL:
                    _sqlMetadataProvider =
                       new MsSqlMetadataProvider(_runtimeConfigProvider,
                           _queryExecutor,
                           _queryBuilder,
                           _sqlMetadataLogger);
                    break;
                case TestCategory.MYSQL:
                    _sqlMetadataProvider =
                       new MySqlMetadataProvider(_runtimeConfigProvider,
                           _queryExecutor,
                           _queryBuilder,
                           _sqlMetadataLogger);
                    break;
                case TestCategory.POSTGRESQL:
                    _sqlMetadataProvider =
                       new PostgreSqlMetadataProvider(_runtimeConfigProvider,
                           _queryExecutor,
                           _queryBuilder,
                           _sqlMetadataLogger);
                    break;
            }

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
            }
            catch (DataApiBuilderException ex)
            {
                // use contains to correctly cover db/user unique error messaging
                Assert.IsTrue(ex.Message.Contains(DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            }
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set up the source fields for the entities.
        /// <code>Check: </code>  Verifies that source object is correctly parsed.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task CheckCorrectParsingForStoredProcedure()
        {
            DatabaseEngine = TestCategory.MSSQL;
            _runtimeConfig = SqlTestHelper.SetupRuntimeConfig(DatabaseEngine);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            SetUpSQLMetadataProvider();

            await _sqlMetadataProvider.InitializeAsync();

            Entity entity = _runtimeConfig.Entities["GetBooks"];
            Assert.AreEqual("get_books", entity.SourceName);
            Assert.AreEqual(SourceType.StoredProcedure, entity.ObjectType);
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
                MsSqlMetadataProvider.ValidateEntityAndGraphQLPathUniqueness(path: entityPath, graphQLGlobalPath: graphQLConfigPath);
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

        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow("__typename", null, true, DisplayName = "Database object with column name that violates GraphQL name rules.")]
        [DataRow("__typename", "typeName", false, DisplayName = "Runtime config entity field mapping/aliasing mitigates violation of GraphQL name rules.")]
        [DataRow("ColumnName", null, false, DisplayName = "Database object with column name conforming to GraphQL name rules.")]
        [DataRow("ColumnName", "__columnName", true, DisplayName = "Database object with column alias violating GraphQL name rules.")]
        public void ValidateGraphQLReservedNaming_DatabaseColumns(string fieldName, string fieldAlias, bool expectsError)
        {
            Dictionary<string, string> columnNameMappings = new();
            columnNameMappings.Add(key: fieldName, value: "ValidColumnName");

            Entity sampleEntity = new(
                Source: JsonSerializer.SerializeToElement("books"),
                Rest: null,
                GraphQL: JsonSerializer.SerializeToElement(true),
                Permissions: new PermissionSetting[] { ConfigurationTests.GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: columnNameMappings
                );

            try
            {
                bool actual = MsSqlMetadataProvider.FieldMeetsGraphQLNameRequirements(sampleEntity, fieldName);
                if (expectsError)
                {
                    Assert.Fail(message: "Failure expected");
                }

                Assert.AreEqual(expected: expectsError, actual);
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
    }
}

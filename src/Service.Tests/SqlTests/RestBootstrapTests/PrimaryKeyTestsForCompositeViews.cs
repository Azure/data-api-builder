// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestBootstrapTests
{
    /// <summary>
    /// Test class to perform tests on MsSql, MySql, PostgreSql for REST to check if the primary key
    /// can be determined for a complex composite view. In case it cannot be determined, the runtime
    /// would fail during boot up.
    /// </summary>
    [TestClass]
    public class PrimaryKeyTestsForCompositeViews : SqlTestBase
    {
        private static readonly string _compositeViewName = "books_authors";
        private static readonly string _compositeViewQuery = $"'CREATE VIEW {_compositeViewName} as SELECT books.title, authors.name, " +
            $"authors.birthdate, books.id as book_id, authors.id as author_id " +
            $"FROM books INNER JOIN book_author_link ON books.id = book_author_link.book_id " +
            $"INNER JOIN authors ON authors.id = book_author_link.author_id'";

        /// <summary>
        /// Test to validate that the runtime fails and throws an exception during bootstrap when the primary
        /// key cannot be determined for a complex composite view for MsSql.
        /// </summary>
        /// <returns></returns>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task MsSqlPrimaryKeyOnComplexCompositeView()
        {
            // Create query to be executed on the database to add the view.
            string compositeViewDbQuery = $"EXEC(" +
                _compositeViewQuery +
                ")";

            await SetupDatabaseAsync(compositeViewDbQuery, TestCategory.MSSQL, isExceptionExpected: true);
        }

        /// <summary>
        /// Test to validate that the runtime fails and throws an exception during bootstrap when the primary
        /// key cannot be determined for a complex composite view for PostgreSql.
        /// </summary>
        /// <returns></returns>
        [TestMethod, TestCategory(TestCategory.POSTGRESQL)]
        public async Task PostgreSqlPrimaryKeyOnComplexCompositeView()
        {
            // Create query to be executed on the database to add the view.
            string compositeViewDbQuery = $"DO $do$ " +
                $"BEGIN " +
                $"EXECUTE(" +
                _compositeViewQuery +
                "); " +
                "END " +
                "$do$";

            await SetupDatabaseAsync(compositeViewDbQuery, TestCategory.POSTGRESQL, isExceptionExpected: true);
        }

        /// <summary>
        /// Test to validate that the runtime boots up successfully when the primary
        /// key can be determined for a complex composite view for MySql.
        /// </summary>
        /// <returns></returns>
        [TestMethod, TestCategory(TestCategory.MYSQL)]
        public async Task MySqlPrimaryKeyOnComplexCompositeView()
        {
            // Create query to be executed on the database to add the view.
            string compositeViewDbQuery = $"prepare stmt4 from " +
                _compositeViewQuery +
                ";" +
                "execute stmt4";
            await SetupDatabaseAsync(compositeViewDbQuery, TestCategory.MYSQL, isExceptionExpected: false);
        }

        /// <summary>
        /// Helper method to setup dependencies to perform tests and setup the database,
        /// i.e. add all the tables and the complex view to the database.
        /// Not all databases are expected to succeed in this operation. i.e. why isExceptionExpected
        /// is different for each database type.
        /// </summary>
        /// <param name="compositeDbViewquery">Query to add composite view to database.</param>
        /// <param name="dbEngine">The database engine. For eg. MsSql.</param>
        /// <param name="isExceptionExpected">Boolean value indicating whether boot up is expected to fail.</param>
        /// <returns></returns>
        private static async Task SetupDatabaseAsync(string compositeDbViewquery, string dbEngine, bool isExceptionExpected)
        {
            // Setup dependencies
            DatabaseEngine = dbEngine;
            string[] customEntity = { _compositeViewName, _compositeViewName, "" };
            if (isExceptionExpected)
            {
                DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                InitializeTestFixture(
                    new List<string> { compositeDbViewquery },
                    new List<string[]> { customEntity }));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual($"Primary key not configured on the given database object {_compositeViewName}", ex.Message);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            }
            else
            {
                await InitializeTestFixture(
                    new List<string> { compositeDbViewquery },
                    new List<string[]> { customEntity });

                // Query to validate the GET operation result.
                string query = @"
                                 SELECT JSON_OBJECT( 'title', title, 'name', name, 'birthdate',
                                 birthdate, 'book_id', book_id, 'author_id', author_id) AS data
                                 FROM (
                                     SELECT *
                                     FROM " + _compositeViewName + @"
                                     WHERE book_id = 1 AND author_id = 123
                                     ) AS subq";

                // Perform GET operation on the view.
                await SetupAndRunRestApiTest(
                    primaryKeyRoute: "book_id/1/author_id/123",
                    queryString: string.Empty,
                    entityNameOrPath: _compositeViewName,
                    sqlQuery: query
                );
            }
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            string dropViewQuery = $"DROP VIEW IF EXISTS {_compositeViewName}";
            await _queryExecutor.ExecuteQueryAsync<object>(dropViewQuery, parameters: null, dataReaderHandler: null, dataSourceName: string.Empty);
        }
    }
}

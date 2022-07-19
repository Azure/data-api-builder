using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Unittests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class BootStrapFailureTest : SqlTestBase
    {
        private static readonly string _compositeViewName = "books_authors";
        /// <summary>
        /// Test to validate that the runtime fails and throws an exception during bootstrap when the primary
        /// key cannot be determined for a database object.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task IndeterministicPrimaryKeyOnDatabaseObject()
        {
            _testCategory = TestCategory.MSSQL;
            _runtimeConfig = SqlTestHelper.LoadConfig(_testCategory).CurrentValue;
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(_runtimeConfig);
            SqlTestHelper.AddMissingEntitiesToConfig(_runtimeConfig, "books_authors");
            SqlTestBase.SetUpSQLMetadataProvider();

            // Add composite view whose primary key cannot be determined.
            string dbQuery = File.ReadAllText($"{_testCategory}Books.sql");
            string compositeViewQuery = $"EXEC('CREATE VIEW {_compositeViewName} as SELECT books.title, authors.[name], " +
                "authors.[birthdate], books.id as book_id, authors.id as author_id " +
                "FROM dbo.books INNER JOIN dbo.book_author_link ON books.[id] = book_author_link.book_id " +
                "INNER JOIN authors ON authors.[id] = book_author_link.author_id')";

            // Execute the query to add view to the database.
            await _queryExecutor.ExecuteQueryAsync(dbQuery + compositeViewQuery, parameters: null);
            try
            {
                DataGatewayException ex = await Assert.ThrowsExceptionAsync<DataGatewayException>(() => _sqlMetadataProvider.InitializeAsync());
                Assert.AreEqual(HttpStatusCode.NotImplemented, ex.StatusCode);
                Assert.AreEqual($"Primary key not configured on the given database object {_compositeViewName}", ex.Message);
            }
            finally
            {
                string dropViewQuery = $"DROP VIEW IF EXISTS {_compositeViewName}";
                await _queryExecutor.ExecuteQueryAsync(dropViewQuery, parameters: null);
            }
        }
    }
}

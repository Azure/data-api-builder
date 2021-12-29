using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class RestApiTestBase : SqlTestBase
    {
        #region Test Fixture Setup
        protected static RestService _restService;
        protected static RestController _restController;
        protected static readonly string _integrationTableName = "books";

        //protected static Dictionary<string, string> _queryMap = new()
        //{
        //    {
        //        "MsSqlFindById",
        //        $"SELECT * FROM { _integrationTableName } " +
        //        $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
        //    },
        //    {
        //        "MsSqlFindByIdTestWithQueryStringFields",
        //        $"SELECT[id], [title] FROM { _integrationTableName } " +
        //        $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
        //    },
        //    {
        //        "MsSqlFindTestWithQueryStringOneField",
        //        $"SELECT [id] FROM { _integrationTableName } " +
        //        $"FOR JSON PATH, INCLUDE_NULL_VALUES"
        //    },
        //    {
        //        "MsSqlFindTestWithQueryStringMultipleFields",
        //        $"SELECT [id], [title] FROM { _integrationTableName } " +
        //        $"FOR JSON PATH, INCLUDE_NULL_VALUES"
        //    },
        //    {
        //        "MsSqlFindTestWithQueryStringAllFields",
        //        $"SELECT * FROM { _integrationTableName } " +
        //        $"FOR JSON PATH, INCLUDE_NULL_VALUES"
        //    },
        //    {
        //        "MsSqlFindTestWithPrimaryKeyContainingForeignKey",
        //        $"SELECT [id], [content] FROM reviews " +
        //        $"WHERE id = 567 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
        //    },
        //    {
        //        "MsSqlFindByIdTestWithInvalidFields",
        //        $"SELECT [id], [name], [type] FROM { _integrationTableName } " +
        //        $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
        //    },
        //    {
        //        "MsSqlFindTestWithInvalidFields",
        //        $"SELECT [id], [name], [type] FROM { _integrationTableName } " +
        //        $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
        //    },
        //    {
        //        "PostgresFindByIdTest",
        //        @"SELECT to_jsonb(subq) AS data
        //                            FROM (
        //                                SELECT *
        //                                FROM " + _integrationTableName + @"
        //                                WHERE id = 2
        //                                ORDER BY id
        //                                LIMIT 1
        //                            ) AS subq"
        //    },
        //    {
        //        "PostgresFindByIdTestWithQueryStringFields",
        //        @"
        //        SELECT to_jsonb(subq) AS data
        //        FROM (
        //            SELECT id, title
        //            FROM " + _integrationTableName + @"
        //            WHERE id = 1
        //            ORDER BY id
        //            LIMIT 1
        //        ) AS subq
        //    "
        //    },
        //    {
        //        "PostgresFindTestWithPrimaryKeyContainingForeignKey",
        //        @"
        //        SELECT to_jsonb(subq) AS data
        //        FROM (
        //            SELECT id, content
        //            FROM reviews" + @"
        //            WHERE id = 567 AND book_id = 1
        //            ORDER BY id
        //            LIMIT 1
        //        ) AS subq
        //    "
        //    },
        //    {
        //        "PostgresFindByIdTestWithInvalidFields",
        //        @"
        //        SELECT to_jsonb(subq) AS data
        //        FROM (
        //            SELECT id, name, type
        //            FROM " + _integrationTableName + @"
        //        ) AS subq
        //    "
        //    }
        //};

        #endregion
        public abstract string GetQuery(string key);

        #region Positive Tests
        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public async virtual void FindByIdTest()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindByIdTest)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async virtual void FindByIdTestWithQueryStringFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: "?_f=id,title",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindByIdTestWithQueryStringFields)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with 1 field
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async virtual void FindTestWithQueryStringOneField()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?_f=id",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringOneField)),
                controller: _restController);

        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with multiple fields
        /// including the field names. Only returns fields designated in the query string.
        /// </summary>
        [TestMethod]
        public async virtual void FindTestWithQueryStringMultipleFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?_f=id,title",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringMultipleFields)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an empty query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async virtual void FindTestWithQueryStringAllFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFields)),
                controller: _restController
            );
        }

        [TestMethod]
        public async virtual void FindTestWithPrimaryKeyContainingForeignKey()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/567/book_id/1",
                queryString: "?_f=id,content",
                entity: "reviews",
                sqlQuery: GetQuery(nameof(FindTestWithPrimaryKeyContainingForeignKey)),
                controller: _restController
            );
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async virtual void FindByIdTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/567/book_id/1",
                queryString: "?_f=id,content",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindByIdTestWithInvalidFields)),
                controller: _restController,
                exception: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has an invalid field
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async virtual void FindTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?_f=id,null",
                entity: _integrationTableName,
                sqlQuery: GetQuery(nameof(FindTestWithInvalidFields)),
                controller: _restController,
                exception: true
            );
        }

        #endregion
    }
}

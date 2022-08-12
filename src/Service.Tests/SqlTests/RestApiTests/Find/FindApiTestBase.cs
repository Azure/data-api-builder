using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Find
{
    /// <summary>
    /// Test GET REST Api validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class FindApiTestBase : RestApiTestBase
    {
        protected static readonly int _numRecordsReturnedFromTieBreakTable = 2;

        public abstract string GetDefaultSchema();
        public abstract string GetDefaultSchemaForEdmModel();

        #region Positive Tests
        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTest()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindByIdTest)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operations on empty result sets
        /// 1. GET an entity with no rows (empty table)
        /// 2. GET an entity with rows, filtered to none by query parameter
        /// Should be a 200 response with an empty array
        /// </summary>
        [TestMethod]
        public async Task FindEmptyResultSet()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _emptyTableEntityName,
                sqlQuery: GetQuery("FindEmptyTable"),
                controller: _restController
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=1 ne 1",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindEmptyResultSetWithQueryFilter"),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the Rest Api to validate that unique unicode
        /// characters work in queries.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task FindOnTableWithUniqueCharacters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationUniqueCharactersEntity,
                sqlQuery: GetQuery("FindOnTableWithUniqueCharacters"),
                controller: _restController);
        }

        ///<summary>
        /// Tests the Rest Api for GET operations on Database Views,
        /// either simple or composite.
        ///</summary>
        [TestMethod]
        public virtual async Task FindOnViews()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entity: _simple_all_books,
                sqlQuery: GetQuery("FindViewAll"),
                controller: _restController
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "categoryid/2/pieceid/1",
                queryString: string.Empty,
                entity: _simple_subset_stocks,
                sqlQuery: GetQuery("FindViewSelected"),
                controller: _restController
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entity: _composite_subset_bookPub,
                sqlQuery: GetQuery("FindViewComposite"),
                controller: _restController
            );
        }

        ///<summary>
        /// Tests the Rest Api for GET operations on Database Views,
        /// either simple or composite,having filter clause.
        ///</summary>
        [TestMethod]
        public virtual async Task FindTestWithQueryStringOnViews()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id ge 4",
                entity: _simple_all_books,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneGeFilterOnView"),
                controller: _restController);

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: "?$select=id,title",
                entity: _simple_all_books,
                sqlQuery: GetQuery("FindByIdTestWithQueryStringFieldsOnView"),
                controller: _restController
            );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=pieceid eq 1",
                entity: _simple_subset_stocks,
                sqlQuery: GetQuery("FindTestWithFilterQueryStringOneEqFilterOnView"),
                controller: _restController);

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (categoryid gt 1)",
                entity: _simple_subset_stocks,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneNotFilterOnView"),
                controller: _restController);

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter= id lt 5",
                entity: _composite_subset_bookPub,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLtFilterOnView"),
                controller: _restController);
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithQueryStringFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: "?$select=id,title",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindByIdTestWithQueryStringFields)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with 1 field
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringOneField()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=id",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringOneField)),
                controller: _restController);

        }

        /// <summary>
        /// Tests the REST Api for Find operation with an empty query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFields)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a single filter, executes
        /// a test for all of the comparison operators and the unary NOT operator.
        /// </summary>
        [TestMethod]
        public async Task FindTestsWithFilterQueryStringOneOpFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq 1",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryStringOneEqFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=2 eq id",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryStringValueFirstOneEqFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id gt 3",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneGtFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id ge 4",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneGeFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 5",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLtFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id le 4",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLeFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id ne 3",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneNeFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (id lt 2)",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneNotFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (title eq null)",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneRightNullEqFilter"),
                controller: _restController);
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=null ne title",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestWithFilterQueryOneLeftNullNeFilter"),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with two filters separated with AND
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringSingleAndFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 3 and id gt 1",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleAndFilter)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with two filters separated with OR
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringSingleOrFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 3 or id gt 4",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleOrFilter)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleAndFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id lt 4 and id gt 1 and title ne 'Awesome book'",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleAndFilters)),
                controller: _restController);

        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq 1 or id eq 2 or id eq 3",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleOrFilters)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND and those comparisons connected with OR.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleAndOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=(id gt 2 and id lt 4) or (title eq 'Awesome book')",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleAndOrFilters)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a filter query string with multiple
        /// comparisons connected with AND OR and including NOT.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringMultipleNotAndOrFilters()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=(not (id lt 3) or id lt 4) or not (title eq 'Awesome book')",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringMultipleNotAndOrFilters)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation where we compare one field
        /// to the bool returned from another comparison.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFilterQueryStringBoolResultFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=id eq (publisher_id gt 1)",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFilterQueryStringSingleAndFilter)),
                controller: _restController,
                exception: true,
                expectedErrorMessage: "A binary operator with incompatible types was detected. " +
                    "Found operand types 'Edm.Int32' and 'Edm.Boolean' for operator kind 'Equal'.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        [TestMethod]
        public async Task FindTestWithPrimaryKeyContainingForeignKey()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/567/book_id/1",
                queryString: "?$select=id,content",
                entity: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithPrimaryKeyContainingForeignKey)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned with a single primary
        /// key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstSingleKeyPagination()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstSingleKeyPagination)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned with multiple column
        /// primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyPagination()
        {
            string after = $"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1",
                entity: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyPagination)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $after to
        /// get the desired page with a single column primary key.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithAfterSingleKeyPagination()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":7,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$after={HttpUtility.UrlEncode(after)}",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithAfterSingleKeyPagination)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $after to
        /// get the desired page with a multi column primary key.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithAfterMultiKeyPagination()
        {
            string after = $"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                entity: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithAfterMultiKeyPagination)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using pagination
        /// to verify that we return an $after cursor that has the
        /// single primary key column.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithPaginationVerifSinglePrimaryKeyInAfter()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$first=1",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithPaginationVerifSinglePrimaryKeyInAfter)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using pagination
        /// to verify that we return an $after cursor that has all the
        /// multiple primary key columns.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithPaginationVerifMultiplePrimaryKeysInAfter()
        {
            string after = $"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$first=1",
                entity: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithPaginationVerifMultiplePrimaryKeysInAfter)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation for all records.
        /// order by title in ascending order.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFieldsOrderByAsc()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=title",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFieldsOrderByAsc)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation for all records
        /// when there is a space in the column name.
        /// order by "ID Number" in ascending order.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringSpaceInNamesOrderByAsc()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby='ID Number'",
                entity: _integrationEntityHasColumnWithSpace,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringSpaceInNamesOrderByAsc)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// Last Name. Validate that the "after" section in the respond
        /// is well formed.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstAndSpacedColumnOrderBy()
        {
            string after = $"[{{\"Value\":\"Belfort\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"brokers\",\"ColumnName\":\"Last Name\"}}," +
                            $"{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"brokers\",\"ColumnName\":\"ID Number\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby='Last Name'",
                entity: _integrationEntityHasColumnWithSpace,
                sqlQuery: GetQuery(nameof(FindTestWithFirstAndSpacedColumnOrderBy)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true

            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation for all records
        /// order by publisher_id in descending order.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithQueryStringAllFieldsOrderByDesc()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=publisher_id desc",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithQueryStringAllFieldsOrderByDesc)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by title,
        /// with a single primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstSingleKeyPaginationAndOrderBy()
        {
            string after = $"[{{\"Value\":\"Also Awesome book\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"title\"}}," +
                            $"{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=title",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstSingleKeyPaginationAndOrderBy)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by id,
        /// the single primary key column in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstSingleKeyIncludedInOrderByAndPagination()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=id",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstSingleKeyIncludedInOrderByAndPagination)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned to two and then
        /// sorting by id.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstTwoOrderByAndPagination()
        {
            string after = SqlPaginationUtil.Base64Encode($"[{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]");
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=2&$orderby=id",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstTwoOrderByAndPagination)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(after)}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned to two and verify
        /// that with tie breaking we form the correct $after,
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy()
        {
            string after = $"[{{\"Value\":\"2001-01-01\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"authors\",\"ColumnName\":\"birthdate\"}}," +
                            $"{{\"Value\":\"Aniruddh\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"authors\",\"ColumnName\":\"name\"}}," +
                            $"{{\"Value\":125,\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"authors\",\"ColumnName\":\"id\"}}]";
            after = $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$first=2&$orderby=birthdate, name, id desc",
                entity: _integrationTieBreakEntity,
                sqlQuery: GetQuery(nameof(FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy)),
                controller: _restController,
                expectedAfterQueryString: after,
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned to two and verifying
        /// that with a $after that tie breaks we return the correct
        /// result.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy()
        {
            string after = "[{\"Value\":\"2001-01-01\",\"Direction\":0,\"ColumnName\":\"birthdate\"}," +
                            "{\"Value\":\"Aniruddh\",\"Direction\":0,\"ColumnName\":\"name\"}," +
                            "{\"Value\":125,\"Direction\":0,\"ColumnName\":\"id\"}]";
            after = $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$first=2&$orderby=birthdate, name, id{after}",
                entity: _integrationTieBreakEntity,
                sqlQuery: GetQuery(nameof(FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy)),
                controller: _restController,
                verifyNumRecords: _numRecordsReturnedFromTieBreakTable
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// id descending, book_id ascending which make up the entire
        /// composite primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination()
        {
            string after = $"[{{\"Value\":569,\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}," +
                            $"{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=id desc, book_id",
                entity: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// book_id, one of the column that make up the composite
        /// primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination()
        {
            string after = $"[{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=book_id",
                entity: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// publisher_id in descending order, which will tie between
        /// 2 records, then sorting by title in descending order to
        /// break the tie.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstAndMultiColumnOrderBy()
        {
            string after = $"[{{\"Value\":2345,\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"publisher_id\"}}," +
                            $"{{\"Value\":\"US history in a nutshell\",\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"title\"}}," +
                            $"{{\"Value\":4,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=publisher_id desc, title desc",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstAndMultiColumnOrderBy)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// publisher_id in descending order, which will tie between
        /// 2 records, then the primary key should be used to break the tie.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstAndTiedColumnOrderBy()
        {
            string after = $"[{{\"Value\":2345,\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"publisher_id\"}}," +
                            $"{{\"Value\":3,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"books\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=publisher_id desc",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestWithFirstAndTiedColumnOrderBy)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation using with $orderby
        /// where if order of the columns in the orderby must be maintained
        /// to generate the correct result.
        /// </summary>
        [TestMethod]
        public async Task FindTestVerifyMaintainColumnOrderForOrderBy()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=id desc, publisher_id",
                entity: _integrationEntityName,
                sqlQuery: GetQuery(nameof(FindTestVerifyMaintainColumnOrderForOrderBy)),
                controller: _restController
            );
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=publisher_id, id desc",
                entity: _integrationEntityName,
                sqlQuery: GetQuery("FindTestVerifyMaintainColumnOrderForOrderByInReverse"),
                controller: _restController
            );

        }

        /// <summary>
        /// Tests the REST Api for Find operation using $first to
        /// limit the number of records returned and then sorting by
        /// content, with multiple column primary key in the table.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstMultiKeyPaginationAndOrderBy()
        {
            string after = $"[{{\"Value\":\"Indeed a great book\",\"Direction\":1,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"content\"}}," +
                            $"{{\"Value\":1,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"book_id\"}}," +
                            $"{{\"Value\":567,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"reviews\",\"ColumnName\":\"id\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=content desc",
                entity: _entityWithCompositePrimaryKey,
                sqlQuery: GetQuery(nameof(FindTestWithFirstMultiKeyPaginationAndOrderBy)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields. Verify that we return the
        /// correct names from the mapping, as well as the names
        /// of the unmapped fields.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithMappedFieldsToBeReturned()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(FindTestWithMappedFieldsToBeReturned)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields. Verify that we return the
        /// correct name from a single mapped field without
        /// unmapped fields included.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithSingleMappedFieldsToBeReturned()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=Scientific Name",
                entity: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(FindTestWithSingleMappedFieldsToBeReturned)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields. Verify that we return the
        /// correct name from a single unmapped field without
        /// mapped fields included.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithUnMappedFieldsToBeReturned()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=treeId",
                entity: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(FindTestWithUnMappedFieldsToBeReturned)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields that are different from another
        /// entity which shares the same source table.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithDifferentMappedFieldsAndFilter()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=fancyName eq 'Tsuga terophylla'",
                entity: _integrationMappingDifferentEntity,
                sqlQuery: GetQuery(nameof(FindTestWithDifferentMappedFieldsAndFilter)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields that are different from another
        /// entity which shares the same source table, and we include
        /// orderby in the request.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithDifferentMappedFieldsAndOrderBy()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=fancyName",
                entity: _integrationMappingDifferentEntity,
                sqlQuery: GetQuery(nameof(FindTestWithDifferentMappedFieldsAndOrderBy)),
                controller: _restController
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields that are different from another
        /// entity which shares the same source table, and we include
        /// orderby in the request, along with pagination.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy()
        {
            string after = $"[{{\"Value\":\"Pseudotsuga menziesii\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"trees\",\"ColumnName\":\"fancyName\"}}," +
                            $"{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"trees\",\"ColumnName\":\"treeId\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=1&$orderby=fancyName",
                entity: _integrationMappingDifferentEntity,
                sqlQuery: GetQuery(nameof(FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy)),
                controller: _restController,
                expectedAfterQueryString: $"&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                paginated: true
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation when the target
        /// entity has mapped fields that are different from another
        /// entity which shares the same source table, and we include
        /// after in the request, along with orderby.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy()
        {
            string after = $"[{{\"Value\":\"Pseudotsuga menziesii\",\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"trees\",\"ColumnName\":\"fancyName\"}}," +
                            $"{{\"Value\":2,\"Direction\":0,\"TableSchema\":\"{GetDefaultSchema()}\",\"TableName\":\"trees\",\"ColumnName\":\"treeId\"}}]";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$orderby=fancyName,treeId&$after={HttpUtility.UrlEncode(SqlPaginationUtil.Base64Encode(after))}",
                entity: _integrationMappingDifferentEntity,
                sqlQuery: GetQuery(nameof(FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy)),
                controller: _restController
            );
        }
        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for Find operation using $first=0
        /// to request 0 records, which should throw a DataApiBuilder
        /// Exception.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithFirstZeroSingleKeyPagination()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$first=0",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid number of items requested, $first must be an integer greater than 0. Actual value: 0",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operations using keywords
        /// of which is not currently supported, verify
        /// we throw a DataApiBuilder exception with the correct
        /// error response.
        /// </summary>
        [DataTestMethod]
        [DataRow("startswith", "(title, 'Awesome')", "eq true")]
        [DataRow("endswith", "(title, 'book')", "eq true")]
        [DataRow("indexof", "(title, 'Awe')", "eq 0")]
        [DataRow("length", "(title)", "gt 5")]
        public async Task FindTestWithUnsupportedFilterKeywords(string keyword, string value, string compareTo)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$filter={keyword}{value} {compareTo}",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "$filter query parameter is not well formed.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
            );
        }

        [TestMethod]
        public virtual async Task FindTestWithInvalidFieldsInQueryStringOnViews()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=pq ge 4",
                entity: _simple_all_books,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"Could not find a property named 'pq' on type 'default_namespace.{_simple_all_books}.{GetDefaultSchemaForEdmModel()}books_view_all'.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=pq le 4",
                entity: _simple_subset_stocks,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"Could not find a property named 'pq' on type 'default_namespace.{_simple_subset_stocks}.{GetDefaultSchemaForEdmModel()}stocks_view_selected'.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );

            //"?$filter=not (categoryid gt 1)",
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$filter=not (title gt 1)",
                entity: _composite_subset_bookPub,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"Could not find a property named 'title' on type 'default_namespace.{_composite_subset_bookPub}.{GetDefaultSchemaForEdmModel()}books_publishers_view_composite'.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with an invalid Primary Key Route.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidPrimaryKeyRoute()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/",
                queryString: string.Empty,
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "The request is invalid since it contains a primary key with no value specified.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with non-explicit Primary Key
        /// explicit FindById: GET /<entity>/<primarykeyname>/<primarykeyvalue>/
        /// implicit FindById: GET /<entity>/<primarykeyvalue>/
        /// Expected behavior: 400 Bad Request
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestImplicitPrimaryKey()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "1",
                queryString: string.Empty,
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Support for url template with implicit primary key field names is not yet added.",
                expectedStatusCode: HttpStatusCode.BadRequest
                );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/5671",
                queryString: "?$select=id,content",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid field to be returned requested: content",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation on an entity that does not exist
        /// No sqlQuery provided as error should be thrown prior to database query
        /// Expects a 404 Not Found error
        ///
        /// Also tests on an entity with a case mismatch (nonexistent since entities are case-sensitive)
        /// I.e. the case of the entity defined in config does not match the case of the entity requested
        /// EX: entity defined as `Book` in config but `book` resource requested (GET https://localhost:5001/book)
        /// </summary>
        [TestMethod]
        public async Task FindNonExistentEntity()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: _nonExistentEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"{_nonExistentEntityName} is not a valid entity.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
            );

            // Case sensitive test
            string integrationEntityNameIncorrectCase = _integrationEntityName.Any(char.IsUpper) ?
                _integrationEntityName.ToLowerInvariant() : _integrationEntityName.ToUpperInvariant();

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entity: integrationEntityNameIncorrectCase,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"{integrationEntityNameIncorrectCase} is not a valid entity.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound.ToString()
            );

        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has an invalid field
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithInvalidFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=id,null",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid Field name: null or white space",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has $select
        /// with no parameters.
        /// </summary>
        [TestMethod]
        public async Task FindTestWithEmptySelectFields()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid Field name: null or white space",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an invalid OrderByDirection.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidOrderByDirection()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=id random",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: HttpUtility.UrlDecode("Syntax error at position 9 in \u0027id random\u0027."),
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an invalid column name for sorting.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidOrderByColumn()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=Pinecone",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"Could not find a property named 'Pinecone' on type 'default_namespace.{_integrationEntityName}.{GetDefaultSchemaForEdmModel()}books'.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an invalid column name for sorting
        /// that contains spaces.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidOrderBySpaceInColumn()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby='Large Pinecone'",
                entity: _integrationEntityHasColumnWithSpace,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"Invalid orderby column requested: Large Pinecone.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Regression test to verify we have the correct exception when an invalid
        /// query param is used.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidQueryParam()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?orderby=id",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"Invalid Query Parameter: orderby",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a null for sorting.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestInvalidOrderByNullQueryParam()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$orderby=null",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "OrderBy property is not supported.",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Verifies that we throw exception when primary key
        /// route contains an exposed name that maps to a
        /// backing column name that does not exist in the
        /// table.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindOneTestWithInvalidMapping()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "hazards/black_mold_spores",
                queryString: string.Empty,
                entity: _integrationBrokenMappingEntity,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "The request is invalid since the primary keys: spores requested were not found in the entity definition.",
                expectedStatusCode: HttpStatusCode.NotFound,
                expectedSubStatusCode: "EntityNotFound"
                );
        }

        /// <summary>
        /// Verifies that we throw exception when field
        /// requested is an exposed name that maps to a
        /// backing column name that does not exist in
        /// the table.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithInvalidMapping()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=hazards",
                entity: _integrationBrokenMappingEntity,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid field to be returned requested: hazards",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: "BadRequest"
                );
        }

        /// <summary>
        /// Validate that we throw exception when we have a mapped
        /// field but we do not use the correct mapping that
        /// has been assigned.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task FindTestWithConflictingMappedFieldsToBeReturned()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=species",
                entity: _integrationMappingEntity,
                sqlQuery: GetQuery(nameof(FindTestWithUnMappedFieldsToBeReturned)),
                controller: _restController,
                exception: true,
                expectedErrorMessage: "Invalid field to be returned requested: species",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with attempts at
        /// Sql Injection in the primary key route.
        /// </summary>
        [DataTestMethod]
        [DataRow(" WHERE 1=1/*", true)]
        [DataRow("id WHERE 1=1/*", true)]
        [DataRow(" UNION SELECT * FROM books/*", true)]
        [DataRow("id UNION SELECT * FROM books/*", true)]
        [DataRow("; SELECT * FROM information_schema.tables/*", true)]
        [DataRow("id; SELECT * FROM information_schema.tables/*", true)]
        [DataRow("; SELECT * FROM v$version/*", true)]
        [DataRow("id; SELECT * FROM v$version/*", true)]
        [DataRow("id; DROP TABLE books;/*", true)]
        [DataRow(" WHERE 1=1--", false)]
        [DataRow("id WHERE 1=1--", false)]
        [DataRow(" UNION SELECT * FROM books--", false)]
        [DataRow("id UNION SELECT * FROM books--", false)]
        [DataRow("; SELECT * FROM information_schema.tables--", false)]
        [DataRow("id; SELECT * FROM information_schema.tables--", false)]
        [DataRow("; SELECT * FROM v$version--", false)]
        [DataRow("id; SELECT * FROM v$version--", false)]
        [DataRow("id; DROP TABLE books;--", false)]
        public async Task FindByIdTestWithSqlInjectionInPKRoute(string sqlInjection, bool slashStar)
        {
            string message = slashStar ? "Support for url template with implicit primary key field names is not yet added." :
                $"Parameter \"{sqlInjection}\" cannot be resolved as column \"id\" with type \"Int32\".";
            await SetupAndRunRestApiTest(
                primaryKeyRoute: $"id/{sqlInjection}",
                queryString: $"?$select=id",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: message,
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with attempts at
        /// Sql Injection in the query string.
        /// </summary>
        [DataTestMethod]
        [DataRow(" WHERE 1=1/*")]
        [DataRow(" WHERE 1=1--")]
        [DataRow("id WHERE 1=1/*")]
        [DataRow("id WHERE 1=1--")]
        [DataRow(" UNION SELECT * FROM books/*")]
        [DataRow(" UNION SELECT * FROM books--")]
        [DataRow("id UNION SELECT * FROM books/*")]
        [DataRow("id UNION SELECT * FROM books--")]
        [DataRow("; SELECT * FROM information_schema.tables/*")]
        [DataRow("; SELECT * FROM information_schema.tables--")]
        [DataRow("id; SELECT * FROM information_schema.tables/*")]
        [DataRow("id; SELECT * FROM information_schema.tables--")]
        [DataRow("; SELECT * FROM v$version/*")]
        [DataRow("; SELECT * FROM v$version--")]
        [DataRow("id; SELECT * FROM v$version/*")]
        [DataRow("id; SELECT * FROM v$version--")]
        [DataRow("id; DROP TABLE books;")]
        public async Task FindByIdTestWithSqlInjectionInQueryString(string sqlInjection)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/5671",
                queryString: $"?$select={sqlInjection}",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"Invalid field to be returned requested: {sqlInjection}",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// Tests the REST Api for Find operation with attempts at
        /// Sql Injection in the query string.
        /// </summary>
        [DataTestMethod]
        [DataRow(" WHERE 1=1/*")]
        [DataRow(" WHERE 1=1--")]
        [DataRow("id WHERE 1=1/*")]
        [DataRow("id WHERE 1=1--")]
        [DataRow(" UNION SELECT * FROM books/*")]
        [DataRow(" UNION SELECT * FROM books--")]
        [DataRow("id UNION SELECT * FROM books/*")]
        [DataRow("id UNION SELECT * FROM books--")]
        [DataRow("; SELECT * FROM information_schema.tables/*")]
        [DataRow("; SELECT * FROM information_schema.tables--")]
        [DataRow("id; SELECT * FROM information_schema.tables/*")]
        [DataRow("id; SELECT * FROM information_schema.tables--")]
        [DataRow("; SELECT * FROM v$version/*")]
        [DataRow("; SELECT * FROM v$version--")]
        [DataRow("id; SELECT * FROM v$version/*")]
        [DataRow("id; SELECT * FROM v$version--")]
        [DataRow("id; DROP TABLE books;")]
        public async Task FindManyTestWithSqlInjectionInQueryString(string sqlInjection)
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: $"?$select={sqlInjection}",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                controller: _restController,
                exception: true,
                expectedErrorMessage: $"Invalid field to be returned requested: {sqlInjection}",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        #endregion
    }
}

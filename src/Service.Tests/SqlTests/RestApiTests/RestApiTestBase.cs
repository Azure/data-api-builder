// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass]
    public abstract class RestApiTestBase : SqlTestBase
    {
        protected static readonly string _integrationEntityName = "Book";
        protected static readonly string _integrationTableName = "books";
        protected static readonly string _integrationPaginationEntityName = "Bookmarks";
        protected static readonly string _integrationPaginationTableName = "bookmarks";
        protected static readonly string _integrationMappedPaginationEntityName = "MappedBookmarks";
        protected static readonly string _integrationMappedPaginationTableName = "mappedbookmarks";
        protected static readonly string _foreignKeyTableName = "publishers";
        protected static readonly string _foreignKeyEntityName = "Publisher";
        protected static readonly string _entityWithCompositePrimaryKey = "Review";
        protected static readonly string _tableWithCompositePrimaryKey = "reviews";
        protected const int STARTING_ID_FOR_TEST_INSERTS = 5001;
        protected static readonly string _integration_NonAutoGenPK_EntityName = "magazine";
        protected static readonly string _integration_NonAutoGenPK_TableName = "magazines";
        protected static readonly string _collisionEntity = "bar_magazine";
        protected static readonly string _collisionTable = "bar.magazines";
        protected static readonly string _integration_AutoGenNonPK_EntityName = "Comic";
        protected static readonly string _integration_AutoGenNonPK_TableName = "comics";
        protected static readonly string _Composite_NonAutoGenPK_TableName = "stocks";
        protected static readonly string _integrationEntityHasColumnWithSpace = "Broker";
        protected static readonly string _integrationTableHasColumnWithSpace = "brokers";
        protected static readonly string _integrationTieBreakEntity = "Author";
        protected static readonly string _integrationTieBreakTable = "authors";
        protected static readonly string _integrationMappingEntity = "Tree";
        protected static readonly string _integrationMappingTable = "trees";
        protected static readonly string _integrationMappingDifferentEntity = "plants";
        protected static readonly string _integrationBrokenMappingEntity = "Fungus";
        protected static readonly string _integrationBrokenMappingTable = "fungi";
        protected static readonly string _integrationUniqueCharactersEntity = "ArtOfWar";
        protected static readonly string _integrationUniqueCharactersTable = "aow";
        protected static readonly string _nonExistentEntityName = "!@$%^&*()_+definitely_nonexistent_entity!@$%^&*()_+";
        protected static readonly string _nonExistentStocksEntityPathName = "asset";
        protected static readonly string _emptyTableEntityName = "Empty";
        protected static readonly string _emptyTableTableName = "empty_table";
        protected static readonly string _entityWithSecurityPolicy = "Revenue";
        protected static readonly string _tableWithSecurityPolicy = "revenues";
        protected static readonly string _simple_all_books = "books_view_all";
        protected static readonly string _book_view_with_key_and_mapping = "books_view_with_mapping";
        protected static readonly string _simple_subset_stocks = "stocks_view_selected";
        protected static readonly string _composite_subset_bookPub = "books_publishers_view_composite";
        protected static readonly string _integrationProcedureFindMany_EntityName = "GetBooks";
        protected static readonly string _integrationProcedureFindMany_ProcName = "get_books";
        protected static readonly string _integrationProcedureFindOne_EntityName = "GetBook";
        protected static readonly string _integrationProcedureFindOne_ProcName = "get_book_by_id";
        protected static readonly string _integrationProcedureInsertOneAndDisplay_EntityName = "InsertAndDisplayAllBooksUnderGivenPublisher";
        protected static readonly string _integrationProcedureDeleteOne_EntityName = "DeleteLastInsertedBook";
        protected static readonly string _integrationMappingDifferentEntityPath = "plants";
        protected static readonly string _Composite_NonAutoGenPK_EntityPath = "commodities";
        protected static readonly string _integrationTypeEntity = "SupportedType";
        protected static readonly string _integrationTypeTable = "type_table";
        protected static readonly string _entityWithReadOnlyFields = "BooksSold";
        protected static readonly string _tableWithReadOnlyFields = "books_sold";
        protected static readonly string _autogenPKEntityWithTrigger = "FteData";
        protected static readonly string _autogenPKTableWithTrigger = "fte_data";
        protected static readonly string _nonAutogenPKEntityWithTrigger = "InternData";
        protected static readonly string _nonAutogenPKTableWithTrigger = "intern_data";
        protected static readonly string _tableWithDateTimePK = "stocks_price";
        protected static readonly string _defaultValueAsBuiltInMethodsEntity = "DefaultBuiltInFunction";
        protected static readonly string _defaultValueAsBuiltInMethodsTable = "default_with_function_table";
        protected static readonly string _tableWithVarcharMax = "journals";
        protected static readonly string _entityWithVarcharMax = "Journal";
        protected static readonly string _entityWithDefaultValues = "Default_Books";
        protected static readonly string _tableWithDefaultValues = "default_books";

        public abstract string GetQuery(string key);

        /// <summary>
        /// Sends several insert-capable upserts for one missing composite key and verifies that
        /// the provider serializes the insert decision. SQL-generation tests validate the exact
        /// lock primitive; this integration test validates its end-to-end REST behavior.
        /// </summary>
        protected static async Task VerifyConcurrentSameKeyUpsertsAsync(
            HttpMethod method,
            int categoryId,
            int requestCount = 8)
        {
            const int pieceId = 7001;
            string endpoint = $"api/{_Composite_NonAutoGenPK_EntityPath}/categoryid/{categoryId}/pieceid/{pieceId}";
            string deleteQuery = $"DELETE FROM {_Composite_NonAutoGenPK_TableName} WHERE categoryid = {categoryId} AND pieceid = {pieceId}";
            await _queryExecutor.ExecuteQueryAsync<object>(
                deleteQuery,
                dataSourceName: string.Empty,
                parameters: null,
                dataReaderHandler: null);

            TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            IEnumerable<Task<HttpResponseMessage>> requestTasks = Enumerable.Range(0, requestCount).Select(async requestNumber =>
            {
                await startGate.Task;
                using HttpRequestMessage request = new(method, endpoint)
                {
                    Content = JsonContent.Create(new
                    {
                        categoryName = "Concurrent upsert",
                        piecesAvailable = requestNumber,
                        piecesRequired = 1
                    })
                };

                return await HttpClient.SendAsync(request);
            });

            Task<HttpResponseMessage>[] startedRequests = requestTasks.ToArray();
            startGate.SetResult();
            HttpResponseMessage[] responses = await Task.WhenAll(startedRequests);

            try
            {
                string unexpectedResponses = string.Join(
                    "; ",
                    await Task.WhenAll(responses
                        .Where(response => response.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
                        .Select(async response => $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}")));

                Assert.AreEqual(
                    1,
                    responses.Count(response => response.StatusCode == HttpStatusCode.Created),
                    $"Exactly one request must insert the missing key. Unexpected responses: {unexpectedResponses}");
                Assert.AreEqual(
                    requestCount - 1,
                    responses.Count(response => response.StatusCode == HttpStatusCode.OK),
                    $"Every request after the insert must update the same row. Unexpected responses: {unexpectedResponses}");

                string countJson = await GetDatabaseResultAsync(
                    $"SELECT COUNT(*) AS count FROM {_Composite_NonAutoGenPK_TableName} WHERE categoryid = {categoryId} AND pieceid = {pieceId}",
                    expectJson: false);
                List<Dictionary<string, int>>? countResult = JsonSerializer.Deserialize<List<Dictionary<string, int>>>(countJson);

                Assert.IsNotNull(countResult);
                Assert.AreEqual(1, countResult[0]["count"], "Concurrent upserts must persist exactly one logical row.");
            }
            finally
            {
                foreach (HttpResponseMessage response in responses)
                {
                    response.Dispose();
                }
            }
        }
    }
}

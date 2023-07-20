// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Patch
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlPatchApiTests : PatchApiTestBase
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                $"SELECT [id], [title], [issue_number] FROM [foo].{ _integration_NonAutoGenPK_TableName } " +
                $"WHERE id = 2 AND [title] = 'Batman Begins' " +
                $"AND [issue_number] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_UniqueCharacters_Test",
                $"SELECT [NoteNum] AS [┬─┬ノ( º _ ºノ)], [DetailAssessmentAndPlanning] AS [始計], " +
                $"[WagingWar] AS [作戰], [StrategicAttack] AS [謀攻] " +
                $"FROM { _integrationUniqueCharactersTable } " +
                $"WHERE [NoteNum] = 2 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_CompositeNonAutoGenPK_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 4 AND [pieceid] = 1 AND [categoryName] = 'Tales' " +
                $"AND [piecesAvailable] = 5 AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_Empty_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 5 AND [pieceid] = 1 AND [categoryName] = '' " +
                $"AND [piecesAvailable] = 5 AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_Default_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 7 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] = 0 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_Mapping_Test",
                $"SELECT [treeId], [species] AS [Scientific Name], [region] AS " +
                $"[United State's Region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [treeId] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertInStocksViewSelected",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } WHERE [categoryid] = 4 " +
                $"AND [pieceid] = 1 AND [categoryName] = 'SciFi' AND [piecesAvailable] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_Nulled_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Test",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 8 AND [title] = 'Heart of Darkness' " +
                $"AND [publisher_id] = 2324 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_IfMatchHeaders_Test",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 AND title = 'The Hobbit Returns to The Shire' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateWithDatabasePolicy",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 100 AND [pieceid] = 99 AND [categoryName] = 'Historical' " +
                $"AND [piecesAvailable]= 4 AND [piecesRequired] = 0 AND [pieceid] != 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertWithDatabasePolicy",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 0 AND [pieceid] = 7 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable]= 4 AND [piecesRequired] = 0 AND ([pieceid] != 6 AND [piecesAvailable] > 0) " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Default_Test",
                $"SELECT [id], [book_id], [content] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE id = 567 AND [book_id] = 1 AND [content] = 'That''s a great book' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_CompositeNonAutoGenPK_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable]= 10 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Empty_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = '' " +
                $"AND [piecesAvailable]= 10 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateWithComputedFieldMissingFromRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 1 AND [book_name] = 'New book' AND [copies_sold] = 50 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateWithTimestampFieldMissingFromRequestBody",
                $"SELECT * FROM {_entityWithReadOnlyFields } WHERE [id] = 1 AND [book_name] = 'Another Awesome Book' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertWithTimestampFieldMissingFromRequestBody",
                $"SELECT * FROM {_entityWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Best seller' " +
                $"AND [copies_sold] = 100 AND [last_sold_on] is NULL AND [last_sold_on_date] is NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateStocksViewSelected",
                $"SELECT [categoryid], [pieceid], [categoryName], [piecesAvailable] " +
                $"FROM {_simple_subset_stocks} WHERE categoryid = 2 AND pieceid = 1 " +
                $"AND [categoryName] = 'Historical' AND [piecesAvailable] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Nulled_Test",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            },
            {
                "PatchOneUpdateAccessibleRowWithSecPolicy",
                $"SELECT [id], [revenue], [category], [accessible_role] FROM { _tableWithSecurityPolicy } " +
                $"WHERE [id] = 1 AND [revenue] = 2000 AND [category] = 'Book' AND [accessible_role] = 'Anonymous' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            }
        };
        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        [TestMethod]
        public async Task PatchOneViewBadRequestTest()
        {
            string expectedErrorMessage = $"View or function '{_defaultSchemaName}.{_composite_subset_bookPub}' is not updatable " +
                                           "because the modification affects multiple base tables.";
            await base.PatchOneViewBadRequestTest(expectedErrorMessage);
        }

        [TestMethod]
        public async Task PatchOneWithTimestampFieldMissingFromRequestBody()
        {
            string requestBody = @"
            {
                ""book_name"": ""Another Awesome Book"",
                ""copies_sold"": 100,
                ""last_sold_on"": null
            }";
            string expectedLocationHeader = $"id/1";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PatchOneUpdateWithTimestampFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );

            requestBody = @"
            {
                ""book_name"": ""Best seller"",
                ""copies_sold"": 100,
                ""last_sold_on"": null
            }";

            expectedLocationHeader = $"id/2";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: expectedLocationHeader,
                    queryString: null,
                    entityNameOrPath: _entityWithReadOnlyFields,
                    sqlQuery: GetQuery("PatchOneInsertWithTimestampFieldMissingFromRequestBody"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.Created,
                    expectedLocationHeader: expectedLocationHeader
                );
        }

        [TestMethod]
        public virtual async Task PatchOneWithTimestampFieldInRequestBody()
        {
            string requestBody = @"
            {
                ""row_version"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/1",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'row_version' provided in request body cannot be assigned a value.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            requestBody = @"
            {
                ""row_version"": null
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: "id/2",
                queryString: string.Empty,
                entityNameOrPath: _entityWithReadOnlyFields,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.UpsertIncremental,
                exceptionExpected: true,
                requestBody: requestBody,
                expectedErrorMessage: "Field 'row_version' provided in request body cannot be assigned a value.",
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }
        #region RestApiTestBase Overrides

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        /// <inheritdoc/>
        [TestMethod]
        public override async Task PatchOneUpdateTestOnTableWithSecurityPolicy()
        {
            string requestBody = @"
            {
                ""revenue"": ""2000""
            }";

            await SetupAndRunRestApiTest(
                    primaryKeyRoute: "id/1",
                    queryString: null,
                    entityNameOrPath: _entityWithSecurityPolicy,
                    sqlQuery: GetQuery("PatchOneUpdateAccessibleRowWithSecPolicy"),
                    operationType: EntityActionOperation.UpsertIncremental,
                    requestBody: requestBody,
                    expectedStatusCode: HttpStatusCode.OK
                );
        }

        #endregion
    }
}

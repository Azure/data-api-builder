// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlInsertApiTests : InsertApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            },
            {
                "InsertOneInSupportedTypes",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id as typeid, short_types, int_types, long_types, string_types, single_types,
                        float_types, decimal_types, boolean_types, datetime_types, bytearray_types, uuid_types
                        FROM " + _integrationTypeTable + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            },
            {
                "InsertOneWithComputedFieldMissingInRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, last_sold_on, last_sold_on_date
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 2 AND book_name = 'Harry Potter' AND copies_sold = 50 AND last_sold_on = '9999-12-31 23:59:59.997'
                        AND last_sold_on_date = '9999-12-31 23:59:59.997'
                    ) AS subq
                "
            },
            {
                "InsertOneUniqueCharactersTest",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""NoteNum"" AS ""┬─┬ノ( º _ ºノ)"", ""DetailAssessmentAndPlanning""
                        AS ""始計"", ""WagingWar"" AS ""作戰"", ""StrategicAttack"" AS ""謀攻""
                        FROM " + _integrationUniqueCharactersTable + @"
                        WHERE ""NoteNum"" = 2
                    ) AS subq
                "
            },
            {
                "InsertOneWithMappingTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""Scientific Name"", ""region""
                            AS ""United State's Region"", ""height""
                        FROM " + _integrationMappingTable + @"
                        WHERE ""treeId"" = 3
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 5 AND pieceid = 2 AND ""categoryName"" = 'Tales'
                            AND ""piecesAvailable"" = 0 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeKeyTableTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND book_id = 1
                    ) AS subq
                "
            },
            {
                "InsertOneWithNullFieldValue",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 1
                    ) AS subq
                "
            },
            {
                "InsertOneInDefaultTestTable",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + (STARTING_ID_FOR_TEST_INSERTS + 1) + @" AND book_id = 2
                    ) AS subq
                "
            },
            {
                "InsertOneWithDefaultValuesAndEmptyRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _tableWithDefaultValues + @"
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery1",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' UNION SELECT * FROM books/*'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery2",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = '; SELECT * FROM information_schema.tables/*'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery3",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'value; SELECT * FROM v$version--'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery4",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'id; DROP TABLE books;'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery5",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' '' UNION SELECT * FROM books/*'
                    ) AS subq
                "
            },
            {
                "InsertOneInBooksViewAll",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _simple_all_books + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            },
            {
                "InsertOneInStocksViewSelected",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable""
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 4 AND pieceid = 1
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "InsertOneWithExcludeFieldsTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title 
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            },
            {
                "InsertOneWithNoReadPermissionsTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title 
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @" AND 0 = 1
                    ) AS subq
                "
            },
            {
                "InsertOneRowWithBuiltInMethodAsDefaultvaluesTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _defaultValueAsBuiltInMethodsTable + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            }
        };

        [TestMethod]
        public async Task InsertOneInViewBadRequestTest()
        {
            string expectedErrorMessage = $"55000: cannot insert into view \"{_composite_subset_bookPub}\"";
            await base.InsertOneInViewBadRequestTest(expectedErrorMessage, isExpectedErrorMsgSubstr: true);
        }

        [TestMethod]
        public async Task InsertOneWithDefaultValuesAndEmptyRequestBody()
        {
            // Validate that we can insert when request body is empty but we have columns that have default values.
            string requestBody = @"
            {
            }";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: string.Empty,
                entityNameOrPath: _entityWithDefaultValues,
                sqlQuery: GetQuery(nameof(InsertOneWithDefaultValuesAndEmptyRequestBody)),
                operationType: EntityActionOperation.Insert,
                exceptionExpected: false,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created
                );
        }

        #region overridden tests
        /// <inheritdoc/>
        [TestMethod]
        public override async Task InsertOneTestViolatingForeignKeyConstraint()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 12345
            }";

            string expectedErrorMessage = "23503: insert or update on table \"books\" violates foreign key" +
                    " constraint \"book_publisher_fk\"";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
        }

        /// <inheritdoc/>
        [TestMethod]
        public override async Task InsertOneTestViolatingUniqueKeyConstraint()
        {
            string requestBody = @"
            {
                ""categoryid"": 1,
                ""pieceid"": 1,
                ""categoryName"": ""SciFi""
            }";

            string expectedErrorMessage = $"23505: duplicate key value violates unique constraint \"{_Composite_NonAutoGenPK_TableName}_pkey\"";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _Composite_NonAutoGenPK_EntityPath,
                sqlQuery: string.Empty,
                operationType: EntityActionOperation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.Conflict,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString(),
                isExpectedErrorMsgSubstr: true
            );
        }

        #endregion

        #region Tests for features yet to be implemented

        [TestMethod]
        [Ignore]
        public override Task InsertOneFailingDatabasePolicy()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task InsertOneInTableWithFieldsInDbPolicyNotPresentInBody()
        {
            throw new NotImplementedException();
        }
        #endregion

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture();
        }

        #endregion

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}

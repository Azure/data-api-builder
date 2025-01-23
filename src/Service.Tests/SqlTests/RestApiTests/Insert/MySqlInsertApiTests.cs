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
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlInsertApiTests : InsertApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneTest",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5001
                    ) AS subq
                "
            },
            {
                "InsertOneInSupportedTypes",
                @"
                    SELECT JSON_OBJECT('typeid', typeid,'bytearray_types', bytearray_types) AS data
                    FROM (
                        SELECT id as typeid, bytearray_types 
                        FROM " + _integrationTypeTable + @"
                        WHERE id = 5001 AND bytearray_types is NULL 
                    ) AS subq
                "
            },
            {
                "InsertOneWithComputedFieldMissingInRequestBody",
                @"
                    SELECT JSON_OBJECT('id', id, 'book_name', book_name, 'copies_sold', copies_sold,
                                        'last_sold_on',last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, DATE_FORMAT(last_sold_on, '%Y-%m-%dT%H:%i:%s') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 2 AND book_name = 'Harry Potter' AND copies_sold = 50
                    ) AS subq
                "
            },
            {
                "InsertOneUniqueCharactersTest",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('┬─┬ノ( º _ ºノ)', NoteNum,
                  '始計', DetailAssessmentAndPlanning, '作戰', WagingWar,
                  '謀攻', StrategicAttack)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationUniqueCharactersTable + @"
                      WHERE NoteNum = 2
                  ) AS subq
                "
            },
            {
                "InsertOneWithMappingTest",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'Scientific Name', species,
                    'United State\'s Region', region)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE treeId = 3
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                      'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 5 AND pieceid = 2 AND categoryName ='Tales' AND piecesAvailable = 0
                        AND piecesRequired = 0
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeKeyTableTest",
                @"
                    SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
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
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 1
                    ) AS subq
                "
            },
            {
                "InsertOneInDefaultTestTable",
                @"
                    SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS + 1}" + @"
                        AND book_id = 2 AND content = 'Its a classic'
                    ) AS subq
                "
            },
            {
                "InsertOneWithDefaultValuesAndEmptyRequestBody",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _tableWithDefaultValues + @"
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery1",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' '' UNION SELECT * FROM books/*'
                    ) AS subq
                "
            },
            {
                "InsertOneWithExcludeFieldsTest",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title) AS data
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
                    SELECT JSON_OBJECT('id', id, 'user_value', user_value, 'current_date', current_date, 'current_timestamp', current_timestamp,
                                        'random_number', random_number, 'next_date', next_date, 'default_string_with_parenthesis', default_string_with_parenthesis,
                                        'default_function_string_with_parenthesis', default_function_string_with_parenthesis, 'default_integer', default_integer,
                                        'default_date_string', default_date_string) AS data
                    FROM (
                        SELECT *
                        FROM " + _defaultValueAsBuiltInMethodsTable + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            }
        };

        [TestMethod]
        [Ignore]
        public void InsertOneInViewBadRequestTest()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Validates we are able to successfully insert with an empty request body into a table
        /// that has default values available for its columns.
        /// </summary>
        /// <returns></returns>
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

            string expectedErrorMessage = "Cannot add or update a child row: a foreign key constraint fails " +
                    $"(`{DatabaseName}`.`books`, CONSTRAINT `book_publisher_fk` FOREIGN KEY (`publisher_id`) REFERENCES" +
                    " `publishers` (`id`) ON DELETE CASCADE)";

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
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
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
            }"
            ;

            string expectedErrorMessage = $"Duplicate entry '1-1' for key '{_Composite_NonAutoGenPK_TableName}.PRIMARY'";

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
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
            );
        }
        #endregion

        #region Tests for features yet to be implemented

        [TestMethod]
        [Ignore]
        public override Task InsertOneInViewTest()
        {
            throw new NotImplementedException();
        }

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

        [TestMethod]
        [Ignore]
        public override Task InsertOneRowWithBuiltInMethodAsDefaultvaluesTest()
        {
            // FIXME: This test is failing because of incorrect SQL query. Issue: https://github.com/Azure/data-api-builder/issues/1696
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
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
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

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}

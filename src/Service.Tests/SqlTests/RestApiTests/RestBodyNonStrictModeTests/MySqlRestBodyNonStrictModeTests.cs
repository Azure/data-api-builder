// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Class containing integration tests for MySql- to validate scenarios when we operate in non-strict mode for REST request body,
    /// i.e. we allow extraneous fields to be present in the request body.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlRestBodyNonStrictModeTests : RestBodyNonStrictModeTests
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneWithExtraneousFieldsInRequestBody",
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
                "InsertOneWithReadOnlyFieldsInRequestBody",
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
                "PutOneWithExtraneousFieldsInRequestBody",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 10
                        AND piecesRequired = 5
                    ) AS subq
                "
            },
            {
                "PutOneUpdateWithComputedFieldInRequestBody",
                @"
                    SELECT JSON_OBJECT('id', id, 'book_name', book_name, 'copies_sold', copies_sold,
                                        'last_sold_on',last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, DATE_FORMAT(last_sold_on, '%Y-%m-%d %H:%i:%s') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 1 AND book_name = 'New book' AND copies_sold = 101 AND last_sold_on is NULL AND last_sold_on_date is NULL
                    ) AS subq
                "
            },
            {
                "PutOneInsertWithComputedFieldInRequestBody",
                @"
                    SELECT JSON_OBJECT('id', id, 'book_name', book_name, 'copies_sold', copies_sold,
                                        'last_sold_on',last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, DATE_FORMAT(last_sold_on, '%Y-%m-%dT%H:%i:%s') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 2 AND book_name = 'New book' AND copies_sold = 101
                    ) AS subq
                "
            },
            {
                "PatchOneWithExtraneousFieldsInRequestBody",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 0
                    ) AS subq
                "
            },
            {
                "PatchOneUpdateWithComputedFieldInRequestBody",
                @"
                    SELECT JSON_OBJECT('id', id, 'book_name', book_name, 'copies_sold', copies_sold,
                                        'last_sold_on',last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, DATE_FORMAT(last_sold_on, '%Y-%m-%d %H:%i:%s') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 1 AND book_name = 'New book' AND copies_sold = 50
                    ) AS subq
                "
            },
            {
                "PatchOneInsertWithComputedFieldInRequestBody",
                @"
                    SELECT JSON_OBJECT('id', id, 'book_name', book_name, 'copies_sold', copies_sold,
                                        'last_sold_on',last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, DATE_FORMAT(last_sold_on, '%Y-%m-%dT%H:%i:%s') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 3 AND book_name = 'New book' AND copies_sold = 50
                    ) AS subq
                "
            }
        };

        [ClassInitialize]
        public static async Task SetupDatabaseAsync(TestContext TestContext)
        {
            DatabaseEngine = TestCategory.MYSQL;

            // Set rest.request-body-strict = false to simulate scenario when we operate in non-strict mode for fields in request body.
            await InitializeTestFixture(isRestBodyStrict: false);
        }

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

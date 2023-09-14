// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Class containing integration tests for MsSql- to validate scenarios when we operate in non-strict mode for REST request body,
    /// i.e. we allow extraneous fields to be present in the request body.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestBodyNonStrictModeTests : RestBodyNonStrictModeTests
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneWithExtraneousFieldsInRequestBody",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithReadOnlyFieldsInRequestBody",
                $"SELECT * FROM {_tableWithReadOnlyFields } WHERE [id] = 2 AND [book_name] = 'Harry Potter' AND [copies_sold] = 50 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneWithExtraneousFieldsInRequestBody",
                $"SELECT [categoryid], [pieceid], [categoryName], [piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 2 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] = 10  AND [piecesRequired] = 5 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneUpdateWithComputedFieldInRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 1 AND [book_name] = 'New book' AND [copies_sold] = 101 AND [last_sold_on] = '2023-09-12 05:30:30' AND [last_sold_on_date] = '2023-09-12 05:30:30' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOneInsertWithComputedFieldInRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 2 AND [book_name] = 'New book' AND [copies_sold] = 101 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneWithExtraneousFieldsInRequestBody",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 1 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneUpdateWithComputedFieldInRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 1 AND [book_name] = 'New book' AND [copies_sold] = 50 " +
                $"AND [last_sold_on] is not NULL AND [last_sold_on_date] is not NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOneInsertWithComputedFieldInRequestBody",
                $"SELECT * FROM { _tableWithReadOnlyFields } " +
                $"WHERE [id] = 3 AND [book_name] = 'New book' AND [copies_sold] = 50 AND " +
                $"[last_sold_on] = '1999-01-08 10:23:54' AND [last_sold_on_date] = '1999-01-08 10:23:54' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            }
        };

        [ClassInitialize]
        public static async Task SetupDatabaseAsync(TestContext TestContext)
        {
            DatabaseEngine = TestCategory.MSSQL;

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

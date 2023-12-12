// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        protected override string MakeQueryOnTypeTable(List<string> columnsToQuery, int id)
        {
            return MakeQueryOnTypeTable(columnsToQuery, filterValue: id.ToString(), filterField: "id");
        }

        protected override string MakeQueryOnTypeTable(
            List<string> queriedColumns,
            string filterValue = "1",
            string filterOperator = "=",
            string filterField = "1",
            string orderBy = "id",
            string limit = "1")
        {
            string format = limit.Equals("1") ? "WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES" : "INCLUDE_NULL_VALUES";
            return @"
                SELECT TOP " + limit + " " + string.Join(", ", queriedColumns) + @"
                FROM type_table AS [table0]
                WHERE " + filterField + " " + filterOperator + " " + filterValue + @"
                ORDER BY " + orderBy + @" asc
                FOR JSON PATH,
                " + format + @"
                    
            ";
        }
    }
}

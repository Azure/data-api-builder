// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

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
            await InitializeTestFixture(context);
        }

        protected override string MakeQueryOnTypeTable(List<string> columnsToQuery, int id)
        {
            return MakeQueryOnTypeTable(columnsToQuery, filterValue: id.ToString(), filterField: "id");
        }

        /// <summary>
        /// Test to validate functioning of GraphQL query with filter and orderby with datetime2 column based on the given parameters.
        /// </summary>
        [DataTestMethod]
        [DataRow(DATETIME2_TYPE, "gt", "\'0001-01-08 10:23:00.9999999\'", "\"0001-01-08 10:23:00.9999999\"", " > ")]
        [DataRow(DATETIME2_TYPE, "gte", "\'0001-01-08 10:23:00.9999999\'", "\"0001-01-08 10:23:00.9999999\"", " >= ")]
        [DataRow(DATETIME2_TYPE, "lt", "\'0002-06-06\'", "\"0002-06-06\"", " < ")]
        [DataRow(DATETIME2_TYPE, "lte", "\'9999-12-31\'", "\"9999-12-31\"", " <= ")]
        [DataRow(DATETIME2_TYPE, "neq", "\'9999-12-31 23:59:59\'", "\"9999-12-31 23:59:59\"", "!=")]
        public async Task QueryTypeColumnFilterAndOrderByDateTime2(string type, string filterOperator, string sqlValue, string gqlValue, string queryOperator)
        {
            if (DatabaseEngine is TestCategory.MYSQL && sqlValue is "\'9999-12-31 23:59:59.9999999\'")
            {
                sqlValue = "\'9999-12-31 23:59:59.0000000\'";
                gqlValue = "\"9999-12-31 23:59:59.0000000\"";
            }

            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
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

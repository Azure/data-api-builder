// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

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

        /// <summary>
        /// MSSQL Single Type Tests.
        /// </summary>
        /// <param name="type">GraphQL Type</param>
        /// <param name="filterOperator">Comparison operator: gt, lt, gte, lte, etc.</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(SINGLE_TYPE, "gt", "-9.3", "-9.3", ">")]
        [DataRow(SINGLE_TYPE, "gte", "-9.2", "-9.2", ">=")]
        [DataRow(SINGLE_TYPE, "lt", ".33", "0.33", "<")]
        [DataRow(SINGLE_TYPE, "lte", ".33", "0.33", "<=")]
        [DataRow(SINGLE_TYPE, "neq", "9.2", "9.2", "!=")]
        [DataRow(SINGLE_TYPE, "eq", "'0.33'", "0.33", "=")]
        [DataTestMethod]
        public async Task MSSQL_real_graphql_single_filter_expectedValues(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// MSSQL filter Type with IN operator Tests.
        /// </summary>
        /// <param name="type">GraphQL Type</param>
        /// <param name="filterOperator">Comparison operator: IN</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(BYTE_TYPE, "1", "1")]
        [DataRow(SHORT_TYPE, "-1", "-1")]
        [DataRow(INT_TYPE, "-1", "-1")]
        [DataRow(LONG_TYPE, "-1", "-1")]
        [DataRow(FLOAT_TYPE, "-9.2", "-9.2")]
        [DataRow(DECIMAL_TYPE, "-9.292929", "-9.292929")]
        [DataRow(UUID_TYPE, "'D1D021A8-47B4-4AE4-B718-98E89C41A161'", "\"D1D021A8-47B4-4AE4-B718-98E89C41A161\"")]
        [DataRow(BOOLEAN_TYPE, "'false'", "false")]
        [DataRow(STRING_TYPE, "lksa;jdflasdf;alsdflksdfkldj", "\"lksa;jdflasdf;alsdflksdfkldj\"")]
        [DataRow(DATETIME_TYPE, "1999-01-08 10:23:54.000", "\"1999-01-08 10:23:54.000\"")]
        [DataTestMethod]
        public async Task MSSQL_real_graphql_in_filter_expectedValues(
            string type,
            string sqlValue,
            string gqlValue)
        {
            if (type == STRING_TYPE)
            {
                sqlValue = $"('{sqlValue}')";
                gqlValue = $"[{gqlValue}]";
            }
            else if (type == DATETIME_TYPE)
            {
                sqlValue = $"({string.Join(", ", sqlValue.Split(';').Select(v => $"'{v.Trim()}'"))})";
                gqlValue = $"[{string.Join(", ", gqlValue.Trim('\"').Split(';').Select(v => $"\"{v}\""))}]";
            }
            else
            {
                sqlValue = $"({sqlValue})";
                gqlValue = $"[{gqlValue}]";
            }

            await QueryTypeColumnFilterAndOrderBy(type, "in", sqlValue, gqlValue, "IN");
        }

        protected override string MakeQueryOnTypeTable(List<DabField> queryFields, int id)
        {
            return MakeQueryOnTypeTable(queryFields, filterValue: id.ToString(), filterField: "id");
        }

        protected override string MakeQueryOnTypeTable(
            List<DabField> queryFields,
            string filterValue = "1",
            string filterOperator = "=",
            string filterField = "1",
            string orderBy = "id",
            string limit = "1")
        {
            string format = limit.Equals("1") ? "WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES" : "INCLUDE_NULL_VALUES";
            return @"
                SELECT TOP " + limit + " " + string.Join(", ", queryFields.Select(field => $"{field.BackingColumnName} AS {field.Alias}")) + @"
                FROM type_table AS [table0]
                WHERE " + filterField + " " + filterOperator + " " + filterValue + @"
                ORDER BY " + orderBy + @" asc
                FOR JSON PATH,
                " + format + @"
                    
            ";
        }
    }
}

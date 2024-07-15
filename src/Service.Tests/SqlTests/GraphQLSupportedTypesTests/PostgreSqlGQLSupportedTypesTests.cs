// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedDateTimeTypes;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Postgres Single Type Test.
        /// Postgres requires conversion of a float value, ex: 0.33 to 'real' type otherwise precision is lost.
        /// </summary>
        /// <param name="type">GraphQL Type</param>
        /// <param name="filterOperator">Comparison operator: gt, lt, gte, lte, etc.</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(SINGLE_TYPE, "gt", "real '-9.3'", "-9.3", ">")]
        [DataRow(SINGLE_TYPE, "gte", "real '-9.2'", "-9.2", ">=")]
        [DataRow(SINGLE_TYPE, "lt", "real '.33'", "0.33", "<")]
        [DataRow(SINGLE_TYPE, "lte", "real '.33'", "0.33", "<=")]
        [DataRow(SINGLE_TYPE, "neq", "real '9.2'", "9.2", "!=")]
        [DataRow(SINGLE_TYPE, "eq", "real '0.33'", "0.33", "=")]
        [DataTestMethod]
        public async Task PG_real_graphql_single_filter_expectedValues(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
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
            string formattedSelect = limit.Equals("1") ? "SELECT to_jsonb(subq3) AS DATA" : "SELECT json_agg(to_jsonb(subq3)) AS DATA";

            return @"
                " + formattedSelect + @"
                FROM
                  (SELECT " + string.Join(", ", queryFields.Select(field => ProperlyFormatTypeTableColumn(field.BackingColumnName) + $" AS {field.Alias}")) + @"
                   FROM public.type_table AS table0
                   WHERE " + filterField + " " + filterOperator + " " + filterValue + @"
                   ORDER BY " + orderBy + @" asc
                   LIMIT " + limit + @") AS subq3
            ";
        }

        protected override bool IsSupportedType(string type)
        {
            return type switch
            {
                BYTE_TYPE => false,
                DATE_TYPE => false,
                SMALLDATETIME_TYPE => false,
                DATETIME2_TYPE => false,
                DATETIMEOFFSET_TYPE => false,
                TIME_TYPE => false,
                LOCALTIME_TYPE => false,
                _ => true
            };
        }

        /// <summary>
        /// Appends parsing logic to some columns which need it
        /// </summary>
        private static string ProperlyFormatTypeTableColumn(string columnName)
        {
            if (columnName.Contains(BYTEARRAY_TYPE.ToLowerInvariant()))
            {
                return $"encode({columnName}, 'base64')";
            }
            else
            {
                return columnName;
            }
        }

        /// <summary>
        /// Bypass DateTime GQL tests for PostreSql
        /// </summary>
        [DataTestMethod]
        [Ignore]
        public new void QueryTypeColumnFilterAndOrderByDateTime(string type, string filterOperator, string sqlValue, string gqlValue, string queryOperator)
        {
            Assert.Inconclusive("Test skipped for PostgreSql.");
        }
    }
}

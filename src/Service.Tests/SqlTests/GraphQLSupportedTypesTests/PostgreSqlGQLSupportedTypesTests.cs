// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HotChocolate.Types.NodaTime;
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

        public override Task InsertMutationInput_DateTimeTypes_ValidRange_ReturnsExpectedValues(string dateTimeGraphQLInput, string expectedResult)
        {
            throw new System.NotImplementedException();
        }
    }
}

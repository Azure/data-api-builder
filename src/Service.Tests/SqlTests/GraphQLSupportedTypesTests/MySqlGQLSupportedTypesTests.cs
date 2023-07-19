// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture(context);
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
            return @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(" + string.Join(", ", queriedColumns.Select(c => $"\"{c}\", {ProperlyFormatTypeTableColumn(c)}")) + @")), '[]') AS `data`
                FROM (
                    SELECT " + string.Join(", ", queriedColumns) + @"
                    FROM type_table AS `table0`
                    WHERE " + filterField + " " + filterOperator + " " + filterValue + @"
                    ORDER BY " + orderBy + @" asc
                    LIMIT " + limit + @"
                    ) AS `subq3`
            ";
        }

        protected override bool IsSupportedType(string type)
        {
            return type switch
            {
                GUID_TYPE => false,
                _ => true
            };
        }

        /// <summary>
        /// Appends parsing logic to some columns which need it
        /// </summary>
        private static string ProperlyFormatTypeTableColumn(string columnName)
        {
            if (columnName.Contains(BOOLEAN_TYPE.ToLowerInvariant()))
            {
                return $"cast({columnName} is true as json)";
            }
            else if (columnName.Contains(BYTEARRAY_TYPE.ToLowerInvariant()))
            {
                return $"to_base64({columnName})";
            }
            else
            {
                return columnName;
            }
        }
    }
}

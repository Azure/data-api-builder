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
            string jsonResultProperties = string.Join(", ", queryFields.Select(field => $"\"{field.Alias}\" , {ProperlyFormatTypeTableColumn(field.BackingColumnName)}"));
            string formattedSelect = limit.Equals("1") ? "SELECT JSON_OBJECT(" + jsonResultProperties + @") AS `data`" :
                "SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(" + jsonResultProperties + @")), '[]') AS `data`";

            return @"
                " + formattedSelect + @"
                FROM (
                    SELECT " + string.Join(", ", queryFields.Select(field => field.BackingColumnName)) + @"
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
                UUID_TYPE => false,
                DATE_TYPE => false,
                SMALLDATETIME_TYPE => false,
                DATETIME2_TYPE => false,
                DATETIMEOFFSET_TYPE => false,
                TIME_TYPE => false,
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

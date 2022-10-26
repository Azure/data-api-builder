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

        protected override string MakeQueryOnTypeTable(List<string> queriedColumns, int id)
        {
            return @"
                SELECT JSON_OBJECT(" + string.Join(", ", queriedColumns.Select(c => $"\"{c}\" , {ProperlyFormatTypeTableColumn(c)}")) + @") AS `data`
                FROM (
                    SELECT " + string.Join(", ", queriedColumns) + @"
                    FROM type_table AS `table0`
                    WHERE id = " + id + @"
                    ORDER BY id asc
                    LIMIT 1
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

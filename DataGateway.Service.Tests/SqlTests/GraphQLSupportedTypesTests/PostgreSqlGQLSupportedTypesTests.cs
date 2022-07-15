using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
    {
        protected override string DatabaseEngine => TestCategory.POSTGRESQL;

        protected override string MakeQueryOnTypeTable(List<string> queriedColumns, int id)
        {
            return @"
                SELECT to_jsonb(subq3) AS DATA
                FROM
                  (SELECT " + string.Join(", ", queriedColumns.Select(c => ProperlyFormatTypeTableColumn(c) + $" AS {c}")) + @"
                   FROM public.type_table AS table0
                   WHERE id = " + id + @"
                   ORDER BY id
                   LIMIT 1) AS subq3
            ";
        }

        protected override bool IsSupportedType(string type, string value = null)
        {
            return type switch
            {
                BYTE_TYPE => false,
                _ => true
            };
        }

        /// <summary>
        /// Appends parsing logic to some columns which need it
        /// </summary>
        private static string ProperlyFormatTypeTableColumn(string columnName)
        {
            if (columnName.Contains(BYTEARRAY_TYPE))
            {
                return $"encode({columnName}, 'base64')";
            }
            else
            {
                return columnName;
            }
        }
    }
}

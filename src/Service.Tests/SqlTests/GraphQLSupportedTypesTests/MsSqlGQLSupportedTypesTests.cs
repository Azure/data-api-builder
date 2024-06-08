// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
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

        public override Task InsertMutationInput_DateTimeTypes_ValidRange_ReturnsExpectedValues(string dateTimeGraphQLInput, string expectedResult)
        {
            throw new System.NotImplementedException();
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

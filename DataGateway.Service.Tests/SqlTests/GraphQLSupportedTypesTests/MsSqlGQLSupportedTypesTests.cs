using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLSupportedTypesTests
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

        protected override string MakeQueryOnTypeTable(List<string> queriedColumns, int id)
        {
            return @"
                SELECT TOP 1 " + string.Join(", ", queriedColumns) + @"
                FROM type_table AS [table0]
                WHERE id = " + id + @"
                ORDER BY id
                FOR JSON PATH,
                    WITHOUT_ARRAY_WRAPPER,
                    INCLUDE_NULL_VALUES
            ";
        }

        /// <summary>
        /// Explicitly declaring a parameter for a bytearray type is not possible due to:
        /// https://stackoverflow.com/questions/29254690/why-does-dbnull-value-require-a-proper-sqldbtype
        /// </summary>
        protected override bool IsSupportedType(string type)
        {
            if (type.Equals(BYTEARRAY_TYPE))
            {
                return false;
            }

            return true;
        }
    }
}

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLPaginationTests
{

    /// <summary>
    /// Only sets up the underlying GraphQLPaginationTestBase to run tests for MsSql
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLPaginationTests : GraphQLPaginationTestBase
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

    }
}

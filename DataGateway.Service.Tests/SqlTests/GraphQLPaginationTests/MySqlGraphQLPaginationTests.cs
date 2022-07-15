using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLPaginationTests
{

    /// <summary>
    /// Only sets up the underlying GraphQLPaginationTestBase to run tests for MySql
    /// </summary>
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLPaginationTests : GraphQLPaginationTestBase
    {
        protected override string DatabaseEngine => TestCategory.MYSQL;
    }
}

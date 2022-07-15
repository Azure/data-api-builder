using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLPaginationTests
{

    /// <summary>
    /// Only sets up the underlying GraphQLPaginationTestBase to run tests for Postgres
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLPaginationTests : GraphQLPaginationTestBase
    {
        protected override string DatabaseEngine => TestCategory.POSTGRESQL;
    }
}

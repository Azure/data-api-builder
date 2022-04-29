using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    /// <summary>
    /// Only sets up the underlying GraphQLPaginationTestBase to run tests for Postgres
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLPaginationTests : GraphQLPaginationTestBase
    {
        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.POSTGRESQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(_queryEngine, _mutationEngine, _metadataStoreProvider, new DocumentCache(), new Sha256DocumentHashProvider(), new Configurations.DataGatewayConfig { DatabaseType = DatabaseType.postgresql }, _runtimeConfigProvider, _sqlMetadataProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }
    }
}

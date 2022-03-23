using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGQLFilterTests : GraphQLFilterTestBase
    {
        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MSSQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider, new Configurations.DataGatewayConfig {  DatabaseType = Configurations.DatabaseType.MsSql });
            _graphQLController = new GraphQLController(_graphQLService);
        }

        protected override string MakeQueryOnBooks(List<string> queriedColumns, string predicate)
        {
            return @"
                SELECT TOP 100 " + string.Join(", ", queriedColumns) + @"
                FROM [books] AS [table0]
                WHERE " + predicate + @"
                ORDER BY [table0].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";
        }
    }
}

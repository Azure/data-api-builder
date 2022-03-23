using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGQLFilterTests : GraphQLFilterTestBase
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
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider, new DocumentCache(), new Sha256DocumentHashProvider(), new Configurations.DataGatewayConfig { DatabaseType = Configurations.DatabaseType.PostgreSql });
            _graphQLController = new GraphQLController(_graphQLService);
        }

        protected override string MakeQueryOnBooks(List<string> queriedColumns, string predicate)
        {
            return @"
                SELECT COALESCE(jsonb_agg(to_jsonb(subq3)), '[]') AS DATA
                FROM
                  (SELECT " + string.Join(", ", queriedColumns) + @"
                   FROM books AS table0
                   WHERE " + predicate + @"
                   ORDER BY table0.id
                   LIMIT 100) AS subq3
            ";
        }
    }
}

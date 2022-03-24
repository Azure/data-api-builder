using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGQLFilterTests : GraphQLFilterTestBase
    {
        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MYSQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _graphQLMetadataProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        protected override string MakeQueryOnBooks(List<string> queriedColumns, string predicate)
        {
            return @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(" + string.Join(", ", queriedColumns.Select(c => $"\"{c}\", {c}")) + @")), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT " + string.Join(", ", queriedColumns) + @"
                    FROM `books` AS `table0`
                    WHERE " + predicate + @"
                    ORDER BY `table0`.`id` LIMIT 100
                    ) AS `subq3`
            ";
        }
    }
}

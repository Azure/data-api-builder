using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
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
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider, new DocumentCache(), new Sha256DocumentHashProvider(), new Configurations.DataGatewayConfig { DatabaseType = Configurations.DatabaseType.MsSql });
            _graphQLController = new GraphQLController(_graphQLService);
        }

        protected override string MakeQueryOn(string table, List<string> queriedColumns, string predicate, List<string> pkColumns = null)
        {
            if (pkColumns == null)
            {
                pkColumns = new() { "id" };
            }

            string orderBy = string.Join(", ", pkColumns.Select(c => $"[table0].[{c}]"));

            return @"
                SELECT TOP 100 " + string.Join(", ", queriedColumns) + @"
                FROM [" + table + @"] AS [table0]
                WHERE " + predicate + @"
                ORDER BY " + orderBy + @"
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";
        }
    }
}

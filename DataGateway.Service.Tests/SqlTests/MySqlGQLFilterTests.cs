using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
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
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider, new DocumentCache(), new Sha256DocumentHashProvider());
            _graphQLController = new GraphQLController(_graphQLService);
        }

        /// <summary>
        /// Gets the default schema for
        /// MySql.
        /// </summary>
        /// <returns></returns>
        protected override string GetDefaultSchema()
        {
            return string.Empty;
        }

        protected override string MakeQueryOn(string table, List<string> queriedColumns, string predicate, string schema, List<string> pkColumns)
        {
            if (pkColumns == null)
            {
                pkColumns = new() { "id" };
            }

            string orderBy = string.Join(", ", pkColumns.Select(c => $"`table0`.`{c}`"));

            return @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(" + string.Join(", ", queriedColumns.Select(c => $"\"{c}\", {c}")) + @")), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT " + string.Join(", ", queriedColumns) + @"
                    FROM `" + table + @"` AS `table0`
                    WHERE " + predicate + @"
                    ORDER BY " + orderBy + @" LIMIT 100
                    ) AS `subq3`
            ";
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGQLFilterTests : GraphQLFilterTestBase
    {
        protected static string DEFAULT_SCHEMA = "dbo";

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
            _graphQLService = new GraphQLService(
                _runtimeConfigProvider,
                _queryEngine,
                _mutationEngine,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _sqlMetadataProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        /// <summary>
        /// Gets the default schema for
        /// MsSql.
        /// </summary>
        /// <returns></returns>
        protected override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        protected override string MakeQueryOn(string table, List<string> queriedColumns, string predicate, string schema, List<string> pkColumns)
        {
            if (pkColumns == null)
            {
                pkColumns = new() { "id" };
            }

            string schemaAndTable = $"[{schema}].[{table}]";
            string orderBy = string.Join(", ", pkColumns.Select(c => $"[table0].[{c}]"));

            return @"
                SELECT TOP 100 " + string.Join(", ", queriedColumns) + @"
                FROM " + schemaAndTable + @" AS [table0]
                WHERE " + predicate + @"
                ORDER BY " + orderBy + @"
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";
        }
    }
}

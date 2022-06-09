using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGQLFilterTests : GraphQLFilterTestBase
    {
        protected static string DEFAULT_SCHEMA = "public";

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
            _graphQLService = new GraphQLService(
                _runtimeConfigPath,
                _queryEngine,
                _mutationEngine,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _sqlMetadataProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        /// <summary>
        /// Gets the default schema for
        /// PostgreSql.
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

            string schemaAndTable = $"{schema}.{table}";
            string orderBy = string.Join(", ", pkColumns.Select(c => $"\"table0\".\"{c}\""));

            return @"
                SELECT COALESCE(jsonb_agg(to_jsonb(subq3)), '[]') AS DATA
                FROM
                  (SELECT " + string.Join(", ", queriedColumns.Select(c => $"\"{c}\"")) + @"
                   FROM " + schemaAndTable + @" AS table0
                   WHERE " + predicate + @"
                   ORDER BY " + orderBy + @"
                   LIMIT 100) AS subq3
            ";
        }
    }
}

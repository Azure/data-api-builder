using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
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
            _graphQLService = new GraphQLService(
                _runtimeConfigProvider,
                _queryEngine,
                _mutationEngine,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _sqlMetadataProvider,
                _authorizationResolver);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        protected override string MakeQueryOnTypeTable(List<string> queriedColumns, int id)
        {
            return @"
                SELECT JSON_OBJECT(" + string.Join(", ", queriedColumns.Select(c => $"\"{c}\" , {ProperlyFormatTypeTableColumn(c)}")) + @") AS `data`
                FROM (
                    SELECT " + string.Join(", ", queriedColumns) + @"
                    FROM type_table AS `table0`
                    WHERE id = " + id + @"
                    ORDER BY id
                    LIMIT 1
                    ) AS `subq3`
            ";
        }

        /// <summary>
        /// Appends parsing logic to some columns which need it
        /// </summary>
        private static string ProperlyFormatTypeTableColumn(string columnName)
        {
            if (columnName.Contains(BOOLEAN_TYPE))
            {
                return $"cast({columnName} is true as json)";
            }
            else if (columnName.Contains(BYTEARRAY_TYPE))
            {
                return $"to_base64({columnName})";
            }
            else
            {
                return columnName;
            }
        }
    }
}

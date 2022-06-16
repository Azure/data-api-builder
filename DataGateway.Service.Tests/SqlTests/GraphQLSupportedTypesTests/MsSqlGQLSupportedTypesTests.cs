using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
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
            _graphQLService = new GraphQLService(
                _runtimeConfigProvider,
                _queryEngine,
                _mutationEngine,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _sqlMetadataProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        protected override string MakeQueryOnTypeTable(List<string> queriedColumns, int id)
        {
            return @"
                SELECT TOP 1 " + string.Join(", ", queriedColumns) + @"
                FROM type_table AS [table0]
                WHERE id = " + id + @"
                ORDER BY id
                FOR JSON PATH,
                    WITHOUT_ARRAY_WRAPPER,
                    INCLUDE_NULL_VALUES
            ";
        }

        /// <summary>
        /// Explicitly declaring a parameter for a bytearray type is not possible due to:
        /// https://stackoverflow.com/questions/29254690/why-does-dbnull-value-require-a-proper-sqldbtype
        /// </summary>
        protected override bool IsSupportedType(string type, string value = null)
        {
            if (type.Equals(BYTEARRAY_TYPE))
            {
                return false;
            }

            return true;
        }
    }
}

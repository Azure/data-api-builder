using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
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
            _graphQLService = new GraphQLService(
                _runtimeConfigPath,
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
                SELECT to_jsonb(subq3) AS DATA
                FROM
                  (SELECT " + string.Join(", ", queriedColumns.Select(c => ProperlyFormatTypeTableColumn(c) + $" AS {c}")) + @"
                   FROM public.type_table AS table0
                   WHERE id = " + id + @"
                   ORDER BY id
                   LIMIT 1) AS subq3
            ";
        }

        protected override bool IsTypeSupportedType(string type, string value = null)
        {
            return type switch
            {
                BYTE_TYPE => false,
                _ => true
            };
        }

        /// <summary>
        /// Appends parsing logic to some columns which need it
        /// </summary>
        private static string ProperlyFormatTypeTableColumn(string columnName)
        {
            if (columnName.Contains(BYTEARRAY_TYPE))
            {
                return $"encode({columnName}, 'base64')";
            }
            else
            {
                return columnName;
            }
        }
    }
}

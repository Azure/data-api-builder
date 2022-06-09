using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass]
    public abstract class GraphQLSupportedTypesTestBase : SqlTestBase
    {
        protected const string TYPE_TABLE = "TypeTable";

        #region Test Fixture Setup
        protected static GraphQLService _graphQLService;
        protected static GraphQLController _graphQLController;

        #endregion

        #region Tests

        [DataTestMethod]
        [DataRow("int", 1)]
        [DataRow("int", 2)]
        [DataRow("int", 3)]
        [DataRow("int", 4)]
        [DataRow("string", 1)]
        [DataRow("string", 2)]
        [DataRow("string", 3)]
        [DataRow("string", 4)]
        [DataRow("float", 1)]
        [DataRow("float", 2)]
        [DataRow("float", 3)]
        [DataRow("float", 4)]
        [DataRow("boolean", 1)]
        [DataRow("boolean", 2)]
        [DataRow("boolean", 3)]
        [DataRow("boolean", 4)]
        public async Task QueryTypeColumn(string type, int id)
        {
            string graphQLQueryName = "supportedType_by_pk";
            string gqlQuery = "{ supportedType_by_pk(id: " + id + ") { " + type + "_types } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { $"{type}_types" }, id);

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [DataTestMethod]
        [DataRow("int", "9999")]
        [DataRow("int", "0")]
        [DataRow("int", "-9999")]
        [DataRow("int", "null")]
        [DataRow("string", "\"aaaaaaaaaa\"")]
        [DataRow("string", "\"\"")]
        [DataRow("string", "null")]
        [DataRow("float", "-3.33")]
        [DataRow("float", "100000.5")]
        [DataRow("float", "null")]
        [DataRow("boolean", "true")]
        [DataRow("boolean", "false")]
        [DataRow("boolean", "null")]
        public async Task InsertIntoTypeColumn(string type, string value)
        {
            string field = $"{type}_types";
            string graphQLQueryName = "createSupportedType";
            string gqlQuery = "mutation{ createSupportedType (item: {" + field + ": " + value + " }){ " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id: 5001);

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController, new() { { "value", value } });
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);

            await ResetDbStateAsync();
        }

        [DataTestMethod]
        [DataRow("int", "9999")]
        [DataRow("int", "0")]
        [DataRow("int", "-9999")]
        [DataRow("int", "null")]
        [DataRow("string", "\"aaaaaaaaaa\"")]
        [DataRow("string", "\"\"")]
        [DataRow("string", "null")]
        [DataRow("float", "-3.33")]
        [DataRow("float", "100000.5")]
        [DataRow("float", "null")]
        [DataRow("boolean", "true")]
        [DataRow("boolean", "false")]
        [DataRow("boolean", "null")]
        public async Task UpdateTypeColumn(string type, string value)
        {
            string field = $"{type}_types";
            string graphQLQueryName = "updateSupportedType";
            string gqlQuery = "mutation{ updateSupportedType (id: 1, item: {" + field + ": " + value + " }){ " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id: 1);

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);

            await ResetDbStateAsync();
        }

        #endregion

        protected abstract string MakeQueryOnTypeTable(List<string> columnsToQuery, int id);
    }
}

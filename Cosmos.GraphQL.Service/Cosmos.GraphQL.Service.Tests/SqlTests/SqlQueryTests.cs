using System.Data.Common;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cosmos.GraphQL.Service.Tests.Sql
{
    [TestClass, TestCategory(TestCategory.MsSql)]
    public class MsSqlQueryBuilderTests : SqlTestBase
    {
        [TestMethod]
        public async Task SingleResultQuery()
        {
            string graphQLQuery = "{\"query\":\"{\\n characterById(id:2){\\n name\\n primaryFunction\\n}\\n}\\n\"}";

            _graphQLController.ControllerContext.HttpContext = GetHttpContextWithBody(graphQLQuery);
            JsonDocument graphQLResult = await _graphQLController.PostAsync();
            JsonElement graphQLResultData = graphQLResult.RootElement.GetProperty("data").GetProperty("characterById");

            JsonDocument sqlResult = JsonDocument.Parse("{ }");
            using DbDataReader reader = _databaseInteractor.QueryExecutor.ExecuteQueryAsync($"SELECT name, primaryFunction FROM {IntegrationTableName} WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER", null).Result;

            if (await reader.ReadAsync())
            {
                sqlResult = JsonDocument.Parse(reader.GetString(0));
            }

            JsonElement sqlResultData = sqlResult.RootElement;

            string actual = graphQLResultData.ToString();
            string expected = sqlResultData.ToString();

            Assert.AreEqual(actual, expected);
        }

        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string graphQLQuery = "{\"query\":\"{\\n  characterList {\\n    name\\n    primaryFunction\\n  }\\n}\\n\"}";
            _graphQLController.ControllerContext.HttpContext = GetHttpContextWithBody(graphQLQuery);
            JsonDocument graphQLResult = await _graphQLController.PostAsync();
            JsonElement graphQLResultData = graphQLResult.RootElement.GetProperty("data").GetProperty("characterList");

            JsonDocument sqlResult = JsonDocument.Parse("{ }");
            DbDataReader reader = _databaseInteractor.QueryExecutor.ExecuteQueryAsync($"SELECT name, primaryFunction FROM character FOR JSON PATH, INCLUDE_NULL_VALUES", null).Result;

            if (await reader.ReadAsync())
            {
                sqlResult = JsonDocument.Parse(reader.GetString(0));
            }

            JsonElement sqlResultData = sqlResult.RootElement;

            string actual = graphQLResultData.ToString();
            string expected = sqlResultData.ToString();

            Assert.AreEqual(expected, actual);
        }
    }
}

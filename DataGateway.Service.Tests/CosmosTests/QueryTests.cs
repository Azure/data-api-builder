using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOS)]
    public class QueryTests : TestBase
    {
        private static readonly string _containerName = Guid.NewGuid().ToString();

        public static readonly string PlanetByPKQuery = @"
query ($id: ID) {
    planet_by_pk (id: $id) {
        id
        name
    }
}";
        public static readonly string PlanetsQuery = @"
query ($first: Int!, $after: String) {
    planets (first: $first, after: $after) {
        items {
            id
            name
        }
        after
        hasNextPage
    }
}";

        private static List<string> _idList;
        private const int TOTAL_ITEM_COUNT = 10;

        [ClassInitialize]
        public static void TestFixtureSetup(TestContext context)
        {
            Init(context);
            Client.CreateDatabaseIfNotExistsAsync(DATABASE_NAME).Wait();
            Client.GetDatabase(DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            _idList = CreateItems(DATABASE_NAME, _containerName, TOTAL_ITEM_COUNT);
            RegisterGraphQLType("Planet", DATABASE_NAME, _containerName);
            RegisterGraphQLType("PlanetConnection", DATABASE_NAME, _containerName, true);
        }

        [TestMethod]
        public async Task GetByPrimaryKeyWithVariables()
        {
            // Run query
            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", PlanetByPKQuery, new() { { "id", id } });

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task GetPaginatedWithVariables()
        {
            // Run query
            JsonElement response = await ExecuteGraphQLRequestAsync("planetList", PlanetsQuery);
            int actualElements = response.GetArrayLength();
            List<string> responseTotal = new();
            ConvertJsonElementToStringList(response, responseTotal);

            // Run paginated query
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string afterToken = null;
            List<string> pagedResponse = new();

            do
            {
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", PlanetsQuery, new() { { "first", pagesize }, { "after", afterToken } });
                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
                ConvertJsonElementToStringList(page.GetProperty("items"), pagedResponse);
            } while (!string.IsNullOrEmpty(afterToken));

            // Validate results
            Assert.AreEqual(actualElements, totalElementsFromPaginatedQuery);
            Assert.IsTrue(responseTotal.SequenceEqual(pagedResponse));
        }

        [TestMethod]
        public async Task GetByPrimaryKeyWithoutVariables()
        {
            // Run query
            string id = _idList[0];
            string query = @$"
query {{
    planet_by_pk (id: ""{id}"") {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task GetPaginatedWithoutVariables()
        {
            // Run query
            JsonElement response = await ExecuteGraphQLRequestAsync("planets", PlanetsQuery);
            int actualElements = response.GetArrayLength();
            List<string> responseTotal = new();
            ConvertJsonElementToStringList(response, responseTotal);

            // Run paginated query
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string afterToken = null;
            List<string> pagedResponse = new();

            do
            {
                string planetConnectionQueryStringFormat = @$"
query {{
    planets (first: {pagesize}, after: {(afterToken == null ? "null" : "\"" + afterToken + "\"")}) {{
        items {{
            id
            name
        }}
        after
        hasNextPage
    }}
}}";
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", planetConnectionQueryStringFormat, variables: new());
                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
                ConvertJsonElementToStringList(page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME), pagedResponse);
            } while (!string.IsNullOrEmpty(afterToken));

            Assert.AreEqual(actualElements, totalElementsFromPaginatedQuery);
            Assert.IsTrue(responseTotal.SequenceEqual(pagedResponse));
        }

        /// <summary>
        /// Query List Type with input parameters
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task GetListTypeWithParameters()
        {
            string id = _idList[0];
            string query = @$"
query {{
    getPlanetListById (id: ""{id}"") {{
        id
        name
    }}
}}";

            JsonElement response = await ExecuteGraphQLRequestAsync("getPlanetListById", query);

            // Validate results
            Assert.AreEqual(1, response.GetArrayLength());
            Assert.AreEqual(id, response[0].GetProperty("id").ToString());
        }

        /// <summary>
        /// Query single item by non-primary key field, found no match
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task GetByNonePrimaryFieldResultNotFound()
        {
            string name = "non-existed name";
            string query = @$"
query {{
    getPlanetByName (name: ""{name}"") {{
        id
        name
    }}
}}";

            JsonElement response = await ExecuteGraphQLRequestAsync("getPlanetByName", query);

            // Validate results
            Assert.IsNull(response.Deserialize<string>());
        }

        /// <summary>
        /// Query single item by non-primary key field, found record back
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task GetByNonPrimaryFieldReturnsResult()
        {
            string name = "test name";
            string query = @$"
query {{
    getPlanetByName (name: ""{name}"") {{
        id
        name
    }}
}}";

            JsonElement response = await ExecuteGraphQLRequestAsync("getPlanetByName", query);

            // Validate results
            Assert.AreEqual(name, response.GetProperty("name").ToString());
        }

        /// <summary>
        /// Query result with nested object
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task GetByPrimaryKeyWithInnerObject()
        {
            // Run query
            string id = _idList[0];
            string query = @$"
query {{
    planetById (id: ""{id}"") {{
        id
        name
        character {{
            id
            name
        }}
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("planetById", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        private static void ConvertJsonElementToStringList(JsonElement ele, List<string> strList)
        {
            if (ele.ValueKind == JsonValueKind.Array)
            {
                JsonElement.ArrayEnumerator enumerator = ele.EnumerateArray();

                while (enumerator.MoveNext())
                {
                    JsonElement prop = enumerator.Current;
                    strList.Add(prop.ToString());
                }
            }
        }

        [ClassCleanup]
        public static void TestFixtureTearDown()
        {
            Client.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}

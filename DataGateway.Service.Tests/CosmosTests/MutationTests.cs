using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOS)]
    public class MutationTests : TestBase
    {
        private static readonly string _containerName = Guid.NewGuid().ToString();
        private static readonly string _createPlanetMutation = @"
                                                mutation ($item: CreatePlanetInput!) {
                                                    createPlanet (item: $item) {
                                                        id
                                                        name
                                                    }
                                                }";
        private static readonly string _deletePlanetMutation = @"
                                                mutation ($id: ID!) {
                                                    deletePlanet (id: $id) {
                                                        id
                                                        name
                                                    }
                                                }";

        /// <summary>
        /// Executes once for the test class.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static void TestFixtureSetup(TestContext context)
        {
            Init(context);
            Client.CreateDatabaseIfNotExistsAsync(DATABASE_NAME).Wait();
            Client.GetDatabase(DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            CreateItems(DATABASE_NAME, _containerName, 10);
            RegisterMutationResolver("createPlanet", DATABASE_NAME, _containerName);
            RegisterMutationResolver("deletePlanet", DATABASE_NAME, _containerName, "Delete");
        }

        [TestMethod]
        public async Task CanCreateItemWithVariables()
        {
            // Run mutation Add planet;
            string id = Guid.NewGuid().ToString();
            var input = new
            {
                id,
                name = "test_name"
            };
            JsonElement response = await ExecuteGraphQLRequestAsync("createPlanet", _createPlanetMutation, new() { { "item", input } });

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task CanDeleteItemWithVariables()
        {
            // Pop an item in to delete
            string id = Guid.NewGuid().ToString();
            var input = new
            {
                id,
                name = "test_name"
            };
            _ = await ExecuteGraphQLRequestAsync("createPlanet", _createPlanetMutation, new() { { "item", input } });

            // Run mutation delete item;
            JsonElement response = await ExecuteGraphQLRequestAsync("deletePlanet", _deletePlanetMutation, new() { { "id", id } });

            // Validate results
            Assert.IsNull(response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task CanCreateItemWithoutVariables()
        {
            // Run mutation Add planet;
            string id = Guid.NewGuid().ToString();
            const string name = "test_name";
            string mutation = $@"
mutation {{
    createPlanet (item: {{ id: ""{id}"", name: ""{name}"" }}) {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("createPlanet", mutation, variables: new());

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task CanDeleteItemWithoutVariables()
        {
            // Pop an item in to delete
            string id = Guid.NewGuid().ToString();
            const string name = "test_name";
            string mutation = $@"
mutation {{
    createPlanet (item: {{ id: ""{id}"", name: ""{name}"" }}) {{
        id
        name
    }}
}}";
            _ = await ExecuteGraphQLRequestAsync("createPlanet", mutation, variables: new());

            // Run mutation delete item;
            string deleteMutation = $@"
mutation {{
    deletePlanet (id: ""{id}"") {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("deletePlanet", deleteMutation, variables: new());

            // Validate results
            Assert.IsNull(response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task MutationMissingInputReturnError()
        {
            // Run mutation Add planet without any input
            string mutation = $@"
mutation {{
    createPlanet {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("createPlanet", mutation, variables: new());
            Assert.AreEqual("inputDict is missing", response[0].GetProperty("message").ToString());
        }

        [TestMethod]
        public async Task MutationMissingRequiredIdReturnError()
        {
            // Run mutation Add planet without id
            const string name = "test_name";
            string mutation = $@"
mutation {{
    createPlanet ( name: ""{name}"") {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("createPlanet", mutation, variables: new());
            Assert.AreEqual("id field is mandatory", response[0].GetProperty("message").ToString());
        }

        /// <summary>
        /// Runs once after all tests in this class are executed
        /// </summary>
        [ClassCleanup]
        public static void TestFixtureTearDown()
        {
            Client.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}

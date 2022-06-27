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
                                                mutation ($id: ID!, $partitionKeyValue: String!) {
                                                    deletePlanet (id: $id, _partitionKeyValue: $partitionKeyValue) {
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
            OverrideEntityContainer("Planet", _containerName);
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
            JsonElement response = await ExecuteGraphQLRequestAsync("deletePlanet", _deletePlanetMutation, new() { { "id", id }, { "partitionKeyValue", id } });

            // Validate results
            Assert.IsNull(response.GetString());
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
    deletePlanet (id: ""{id}"", _partitionKeyValue: ""{id}"") {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("deletePlanet", deleteMutation, variables: new());

            // Validate results
            Assert.IsNull(response.GetString());
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
            string errorMessage = response[0].GetProperty("message").ToString();
            Assert.IsTrue(errorMessage.Contains("The argument `item` is required."), $"The actual error is {errorMessage}");
        }

        [TestMethod]
        public async Task MutationMissingRequiredIdReturnError()
        {
            // Run mutation Add planet without id
            const string name = "test_name";
            string mutation = $@"
mutation {{
    createPlanet (item: {{ name: ""{name}"" }}) {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("createPlanet", mutation, variables: new());
            Assert.AreEqual("id field is mandatory", response[0].GetProperty("message").ToString());
        }

        [TestMethod]
        public async Task CanUpdateItemWithoutVariables()
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
            _ = await ExecuteGraphQLRequestAsync("createPlanet", mutation, variables: new());

            const string newName = "new_name";
            mutation = $@"
mutation {{
    updatePlanet (id: ""{id}"", _partitionKeyValue: ""{id}"", item: {{ id: ""{id}"", name: ""{newName}"" }}) {{
        id
        name
    }}
}}";

            JsonElement response = await ExecuteGraphQLRequestAsync("updatePlanet", mutation, variables: new());

            // Validate results
            Assert.AreEqual(newName, response.GetProperty("name").GetString());
            Assert.AreNotEqual(name, response.GetProperty("name").GetString());
        }

        [TestMethod]
        public async Task CanUpdateItemWithVariables()
        {
            // Run mutation Add planet;
            string id = Guid.NewGuid().ToString();
            var input = new
            {
                id,
                name = "test_name"
            };
            _ = await ExecuteGraphQLRequestAsync("createPlanet", _createPlanetMutation, new() { { "item", input } });

            const string newName = "new_name";
            string mutation = @"
mutation ($id: ID!, $partitionKeyValue: String!, $item: UpdatePlanetInput!) {
    updatePlanet (id: $id, _partitionKeyValue: $partitionKeyValue, item: $item) {
        id
        name
     }
}";
            var update = new
            {
                id = id,
                name = "new_name"
            };

            JsonElement response = await ExecuteGraphQLRequestAsync("updatePlanet", mutation, variables: new() { { "id", id }, { "partitionKeyValue", id }, { "item", update } });

            // Validate results
            Assert.AreEqual(newName, response.GetProperty("name").GetString());
            Assert.AreNotEqual(input.name, response.GetProperty("name").GetString());
        }

        [TestMethod]
        public async Task MutationMissingRequiredPartitionKeyValueReturnError()
        {
            // Run mutation Add planet without id
            string id = Guid.NewGuid().ToString();
            string mutation = $@"
mutation {{
    deletePlanet (id: ""{id}"") {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("deletePlanet", mutation, variables: new());
            Assert.AreEqual("The argument `_partitionKeyValue` is required.", response[0].GetProperty("message").ToString());
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

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class MutationTests : TestBase
    {
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
        /// Executes once for the test.
        /// </summary>
        /// <param name="context"></param>
        [TestInitialize]
        public void TestFixtureSetup()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().GetDefaultDataSourceName()];
            cosmosClient.CreateDatabaseIfNotExistsAsync(DATABASE_NAME).Wait();
            cosmosClient.GetDatabase(DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            CreateItems(DATABASE_NAME, _containerName, 10);
        }

        [TestMethod]
        public async Task CanCreateItemWithVariables()
        {
            // Run mutation Add planet;
            string id = Guid.NewGuid().ToString();
            var input = new
            {
                id,
                name = "test_name",
                stars = new[] { new { id = "TestStar" } }
            };
            JsonElement response = await ExecuteGraphQLRequestAsync("createPlanet", _createPlanetMutation, new() { { "item", input } });

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
            Assert.AreEqual("test_name", response.GetProperty("name").GetString());
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
    createPlanet (item: {{ id: ""{id}"", name: ""{name}"", stars: [{{ id: ""{id}"" }}] }}) {{
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
            Assert.AreEqual("`id` is a required field and cannot be null.", response[0].GetProperty("message").ToString());
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
    updatePlanet (id: ""{id}"", _partitionKeyValue: ""{id}"", item: {{ id: ""{id}"", name: ""{newName}"", stars: [{{ id: ""{id}"" }}] }}) {{
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
                name = "new_name",
                stars = new[] { new { id = "TestStar" } }
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
        /// Mutation can be performed on the authorized fields because the
        /// field `id` is an included field for the create operation on the anonymous role defined
        /// for entity 'earth'
        /// </summary>
        [TestMethod]
        public async Task CanCreateItemWithAuthorizedFields()
        {
            // Run mutation Add Earth;
            string id = Guid.NewGuid().ToString();
            string mutation = $@"
mutation {{
    createEarth (item: {{ id: ""{id}"" }}) {{
        id
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("createEarth", mutation, variables: new());

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        /// <summary>
        /// Mutation performed on the unauthorized fields throws permission denied error because the
        /// field `name` is an excluded field for the create operation on the anonymous role defined
        /// for entity 'earth'
        /// </summary>
        [TestMethod]
        public async Task CreateItemWithUnauthorizedFieldsReturnsError()
        {
            // Run mutation Add Earth;
            string id = Guid.NewGuid().ToString();
            const string name = "test_name";
            string mutation = $@"
mutation {{
    createEarth (item: {{ id: ""{id}"", name: ""{name}"" }}) {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("createEarth", mutation, variables: new());

            // Validate the result contains the GraphQL authorization error code.
            string errorMessage = response.ToString();
            Assert.IsTrue(errorMessage.Contains(DataApiBuilderException.GRAPHQL_MUTATION_FIELD_AUTHZ_FAILURE));
        }

        /// <summary>
        /// Mutation performed on the unauthorized fields throws permission denied error because the
        /// wildcard is used in the excluded field for the update operation on the anonymous role defined
        /// for entity 'earth'
        /// </summary>
        [TestMethod]
        public async Task UpdateItemWithUnauthorizedWildCardReturnsError()
        {
            // Run mutation Update Earth;
            string id = Guid.NewGuid().ToString();
            string mutation = @"
mutation ($id: ID!, $partitionKeyValue: String!, $item: UpdateEarthInput!) {
    updateEarth (id: $id, _partitionKeyValue: $partitionKeyValue, item: $item) {
        id
        name
     }
}";
            var update = new
            {
                id = id,
                name = "new_name"
            };

            JsonElement response = await ExecuteGraphQLRequestAsync("updateEarth", mutation, variables: new() { { "id", id }, { "partitionKeyValue", id }, { "item", update } });

            // Validate the result contains the GraphQL authorization error code.
            string errorMessage = response.ToString();
            Assert.IsTrue(errorMessage.Contains(DataApiBuilderException.GRAPHQL_MUTATION_FIELD_AUTHZ_FAILURE));
        }

        /// <summary>
        /// Validates that a create mutation with only __typename in the selection set returns the
        /// right type
        /// </summary>
        [TestMethod]
        public async Task CreateMutationWithOnlyTypenameInSelectionSet()
        {
            string graphQLMutation = @"
                mutation ($item: CreatePlanetInput!) {
                    createPlanet (item: $item) {
                        __typename
                    }
                }";

            // Construct the inputs required for the mutation
            string id = Guid.NewGuid().ToString();
            var input = new
            {
                id,
                name = "test_name",
                stars = new[] { new { id = "TestStar" } }
            };
            JsonElement response = await ExecuteGraphQLRequestAsync("createPlanet", graphQLMutation, new() { { "item", input } });

            // Validate results
            string expected = @"Planet";
            string actual = response.GetProperty("__typename").Deserialize<string>();
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Validates that an update mutation with only __typename in the selection set returns the
        /// right type
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationWithOnlyTypenameInSelectionSet()
        {
            // Create the item with a known id to execute an update mutation against it
            string id = Guid.NewGuid().ToString();
            var input = new
            {
                id,
                name = "test_name"
            };

            _ = await ExecuteGraphQLRequestAsync("createPlanet", _createPlanetMutation, new() { { "item", input } });

            string mutation = @"
                mutation ($id: ID!, $partitionKeyValue: String!, $item: UpdatePlanetInput!) {
                    updatePlanet (id: $id, _partitionKeyValue: $partitionKeyValue, item: $item) {
                        __typename
                    }
                }";

            // Construct the inputs required for the update mutation
            var update = new
            {
                id,
                name = "new_name",
                stars = new[] { new { id = "TestStar" } }
            };

            // Execute the update mutation
            JsonElement response = await ExecuteGraphQLRequestAsync("updatePlanet", mutation, variables: new() { { "id", id }, { "partitionKeyValue", id }, { "item", update } });

            // Validate results
            string expected = @"Planet";
            string actual = response.GetProperty("__typename").Deserialize<string>();
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Runs once after all tests in this class are executed
        /// </summary>
        [TestCleanup]
        public void TestFixtureTearDown()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().GetDefaultDataSourceName()];
            cosmosClient.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}

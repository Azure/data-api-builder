// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Azure.DataApiBuilder.Config.NamingPolicies;
    using Azure.DataApiBuilder.Config.ObjectModel;
    using Azure.DataApiBuilder.Core.Authorization;
    using Azure.DataApiBuilder.Core.Configurations;
    using Azure.DataApiBuilder.Core.Resolvers;
    using Azure.DataApiBuilder.Service.Exceptions;
    using Azure.DataApiBuilder.Service.Tests.Configuration;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        /// For mutation operations, both the respective operation(create/update/delete) + read permissions are needed to receive a valid response.
        /// In this test, Anonymous role is configured with only create permission.
        /// So, a create mutation executed in the context of Anonymous role is expected to result in
        /// 1) Creation of a new item in the database
        /// 2) An error response containing the error message : "The mutation operation {operation_name} was successful but the current user is unauthorized to view the response due to lack of read permissions"
        ///
        /// A create mutation operation in the context of Anonymous role is executed and the expected error message is validated.
        /// Authenticated role has read permission configured. A pk query is executed in the context of Authenticated role to validate that a new
        /// record was created in the database.
        /// </summary>
        [TestMethod]
        public async Task ValidateErrorMessageForMutationWithoutReadPermission()
        {
            const string SCHEMA = @"
type Planet @model {
    id : ID!,
    name : String,
    age : Int,
}";
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: false);
            Dictionary<string, JsonElement> dbOptions = new();
            HyphenatedNamingPolicy namingPolicy = new();

            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database)), JsonSerializer.SerializeToElement("graphqldb"));
            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container)), JsonSerializer.SerializeToElement(_containerName));
            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema)), JsonSerializer.SerializeToElement("custom-schema.gql"));
            DataSource dataSource = new(DatabaseType.CosmosDB_NoSQL,
                ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.COSMOSDBNOSQL), dbOptions);

            EntityAction createAction = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: new());

            EntityAction readAction = new(
                Action: EntityActionOperation.Read,
                Fields: null,
                Policy: new());

            EntityAction deleteAction = new(
                Action: EntityActionOperation.Delete,
                Fields: null,
                Policy: new());

            EntityPermission[] permissions = new[] {new EntityPermission( Role: AuthorizationResolver.ROLE_ANONYMOUS , Actions: new[] { createAction }),
                       new EntityPermission( Role: AuthorizationResolver.ROLE_AUTHENTICATED , Actions: new[] { readAction, createAction, deleteAction })};

            Entity entity = new(Source: new($"graphqldb.{_containerName}", null, null, null),
                                  Rest: null,
                                  GraphQL: new(Singular: "Planet", Plural: "Planets"),
                                  Permissions: permissions,
                                  Relationships: null,
                                  Mappings: null);

            string entityName = "Planet";
            RuntimeConfig configuration = ConfigurationTests.InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, entityName);

            const string CUSTOM_CONFIG = "custom-config.json";
            const string CUSTOM_SCHEMA = "custom-schema.gql";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());
            File.WriteAllText(CUSTOM_SCHEMA, SCHEMA);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}",
            };

            string id = Guid.NewGuid().ToString();
            string authToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken();
            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                try
                {
                    var input = new
                    {
                        id,
                        name = "test_name",
                    };

                    // A create mutation operation is executed in the context of Anonymous role. The Anonymous role has create action configured but lacks
                    // read action. As a result, a new record should be created in the database but the mutation operation should return an error message.
                    JsonElement mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                        client,
                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                        query: _createPlanetMutation,
                        queryName: "createPlanet",
                        variables: new() { { "item", input } },
                        clientRoleHeader: null
                        );

                    Assert.IsTrue(mutationResponse.ToString().Contains("The mutation operation createPlanet was successful but the current user is unauthorized to view the response due to lack of read permissions"));

                    // pk_query is executed in the context of Authenticated role to validate that the create mutation executed in the context of Anonymous role
                    // resulted in the creation of a new record in the database.
                    string graphQLQuery = @$"
query {{
    planet_by_pk (id: ""{id}"") {{
        id
        name
    }}
}}";
                    string queryName = "planet_by_pk";

                    JsonElement queryResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                                                client,
                                                server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                                query: graphQLQuery,
                                                queryName: queryName,
                                                variables: null,
                                                authToken: authToken,
                                                clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED);

                    Assert.IsFalse(queryResponse.TryGetProperty("errors", out _));
                }
                finally
                {
                    // Clean-up steps. The record created by the create mutation operation is deleted to reset the database
                    // back to its original state.
                    _ = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                        client,
                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                        query: _deletePlanetMutation,
                        queryName: "deletePlanet",
                        variables: new() { { "id", id }, { "partitionKeyValue", id } },
                        authToken: authToken,
                        clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED);
                }
            }
        }

        /// <summary>
        /// For mutation operations, the respective mutation operation type(create/update/delete) + read permissions are needed to receive a valid response.
        /// For graphQL requests, if read permission is configured for Anonymous role, then it is inherited by other roles.
        /// In this test, Anonymous role has read permission configured. Authenticated role has only create permission configured.
        /// A create mutation operation is executed in the context of Authenticated role and the response is expected to have no errors because
        /// the read permission is inherited from Anonymous role.
        /// </summary>
        [TestMethod]
        public async Task ValidateInheritanceOfReadPermissionFromAnonymous()
        {
            const string SCHEMA = @"
type Planet @model {
    id : ID!,
    name : String,
    age : Int,
}";
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: false);
            Dictionary<string, JsonElement> dbOptions = new();
            HyphenatedNamingPolicy namingPolicy = new();

            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database)), JsonSerializer.SerializeToElement("graphqldb"));
            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container)), JsonSerializer.SerializeToElement(_containerName));
            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema)), JsonSerializer.SerializeToElement("custom-schema.gql"));
            DataSource dataSource = new(DatabaseType.CosmosDB_NoSQL,
                ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.COSMOSDBNOSQL), dbOptions);

            EntityAction createAction = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: new());

            EntityAction readAction = new(
                Action: EntityActionOperation.Read,
                Fields: null,
                Policy: new());

            EntityAction deleteAction = new(
                Action: EntityActionOperation.Delete,
                Fields: null,
                Policy: new());

            EntityPermission[] permissions = new[] {new EntityPermission( Role: AuthorizationResolver.ROLE_ANONYMOUS , Actions: new[] { createAction, readAction, deleteAction }),
                       new EntityPermission( Role: AuthorizationResolver.ROLE_AUTHENTICATED , Actions: new[] { createAction })};

            Entity entity = new(Source: new($"graphqldb.{_containerName}", null, null, null),
                                  Rest: null,
                                  GraphQL: new(Singular: "Planet", Plural: "Planets"),
                                  Permissions: permissions,
                                  Relationships: null,
                                  Mappings: null);

            string entityName = "Planet";
            RuntimeConfig configuration = ConfigurationTests.InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, entityName);

            const string CUSTOM_CONFIG = "custom-config.json";
            const string CUSTOM_SCHEMA = "custom-schema.gql";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());
            File.WriteAllText(CUSTOM_SCHEMA, SCHEMA);

            string id = Guid.NewGuid().ToString();
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                try
                {
                    var input = new
                    {
                        id,
                        name = "test_name",
                    };

                    // A create mutation operation is executed in the context of Authenticated role and the response is expected to be a valid
                    // response without any errors.
                    JsonElement mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                        client,
                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                        query: _createPlanetMutation,
                        queryName: "createPlanet",
                        variables: new() { { "item", input } },
                        authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(),
                        clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED
                        );
                        
                    Assert.IsFalse(mutationResponse.TryGetProperty("errors", out _));
                }
                finally
                {
                    // Clean-up steps. The record created by the create mutation operation is deleted to reset the database
                    // back to its original state.
                    _ = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                        client,
                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                        query: _deletePlanetMutation,
                        queryName: "deletePlanet",
                        variables: new() { { "id", id }, { "partitionKeyValue", id } },
                        clientRoleHeader: null);
                }
            }
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

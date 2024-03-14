// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        private const string USER_NOT_AUTHORIZED = "The current user is not authorized to access this resource";
        private const string NO_ERROR_MESSAGE = null;

        /// <summary>
        /// Executes once for the test.
        /// </summary>
        /// <param name="context"></param>
        [TestInitialize]
        public void TestFixtureSetup()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
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
        /// Create Mutation performed on the fields with different auth permissions
        /// It throws permission denied error if role doesn't have permission to perform the operation
        /// </summary>
        [TestMethod]
        [DataRow("field-mutation-with-read-permission", DataApiBuilderException.GRAPHQL_MUTATION_FIELD_AUTHZ_FAILURE, DisplayName = "AuthZ failure for create mutation because of reference to excluded/disallowed fields.")]
        [DataRow("authenticated", MutationTests.NO_ERROR_MESSAGE, DisplayName = "AuthZ success when role has no create/read operation restrictions.")]
        [DataRow("only-create-role", "The mutation operation createEarth was successful " +
            "but the current user is unauthorized to view the response due to lack of read permissions", DisplayName = "Successful create operation but AuthZ failure for read when role has ONLY create permission and NO read permission.")]
        [DataRow("wildcard-exclude-fields-role", DataApiBuilderException.GRAPHQL_MUTATION_FIELD_AUTHZ_FAILURE, DisplayName = "AuthZ failure for create mutation because of reference to excluded/disallowed field using wildcard.")]
        [DataRow("only-update-role", MutationTests.USER_NOT_AUTHORIZED, DisplayName = "AuthZ failure when create permission is NOT there.")]
        public async Task CreateItemWithAuthPermissions(string roleName, string expectedErrorMessage)
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
            string authtoken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: roleName);
            JsonElement response = await ExecuteGraphQLRequestAsync("createEarth", mutation, variables: new(), authToken: authtoken, clientRoleHeader: roleName);

            // Validate the result contains the GraphQL authorization error code.
            Console.WriteLine(response.ToString());
            if (string.IsNullOrEmpty(expectedErrorMessage))
            {
                Assert.AreEqual(id, response.GetProperty("id").GetString());
            }
            else
            {
                // Validate the result contains the GraphQL authorization error code.
                string errorMessage = response.ToString();
                Assert.IsTrue(errorMessage.Contains(expectedErrorMessage));
            }
        }

        /// <summary>
        /// Update Mutation performed on the fields with different auth permissions
        /// It throws permission denied error if role doesn't have permission to perform the operation
        /// </summary>
        [TestMethod]
        [DataRow("field-mutation-with-read-permission", DataApiBuilderException.GRAPHQL_MUTATION_FIELD_AUTHZ_FAILURE, DisplayName = "AuthZ failure for update mutation because of reference to excluded/disallowed fields.")]
        [DataRow("authenticated", NO_ERROR_MESSAGE, DisplayName = "AuthZ success when role has no update/read operation restrictions.")]
        [DataRow("only-update-role", "The mutation operation updateEarth was successful " +
            "but the current user is unauthorized to view the response due to lack of read permissions", DisplayName = "AuthZ failure  but sucessful operation where role has ONLY update permission and NO read permission.")]
        [DataRow("wildcard-exclude-fields-role", DataApiBuilderException.GRAPHQL_MUTATION_FIELD_AUTHZ_FAILURE, DisplayName = "AuthZ failure for update mutation because of reference to excluded/disallowed field using wildcard.")]
        [DataRow("only-create-role", MutationTests.USER_NOT_AUTHORIZED, DisplayName = "AuthZ failure when update permission is NOT there.")]
        public async Task UpdateItemWithAuthPermissions(string roleName, string expectedErrorMessage)
        {
            // Create an item with "Authenticated" role
            string id = Guid.NewGuid().ToString();
            const string name = "test_name";
            string createMutation = $@"
mutation {{
    createEarth (item: {{ id: ""{id}"", name: ""{name}"" }}) {{
        id
        name
    }}
}}";

            JsonElement createResponse = await ExecuteGraphQLRequestAsync("createEarth", createMutation,
                variables: new(),
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: AuthorizationType.Authenticated.ToString()),
                clientRoleHeader: AuthorizationType.Authenticated.ToString());

            // Making sure item is created successfully
            Assert.AreEqual(id, createResponse.GetProperty("id").GetString());

            // Run mutation Update Earth;
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

            string authtoken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: roleName);
            JsonElement response = await ExecuteGraphQLRequestAsync(
                queryName: "updateEarth",
                query: mutation,
                variables: new() { { "id", id }, { "partitionKeyValue", id }, { "item", update } },
                authToken: authtoken,
                clientRoleHeader: roleName);

            Console.WriteLine(response.ToString());
            if (string.IsNullOrEmpty(expectedErrorMessage))
            {
                Assert.AreEqual(id, response.GetProperty("id").GetString());
            }
            else
            {
                // Validate the result contains the GraphQL authorization error code.
                string errorMessage = response.ToString();
                Assert.IsTrue(errorMessage.Contains(expectedErrorMessage));
            }
        }

        /// <summary>
        /// Delete Mutation performed on the fields with different auth permissions
        /// It throws permission denied error if role doesn't have permission to perform the operation
        /// </summary>
        [TestMethod]
        [DataRow("field-mutation-with-read-permission", MutationTests.NO_ERROR_MESSAGE, DisplayName = "AuthZ success and blank response for delete mutation because of reference to excluded/disallowed fields.")]
        [DataRow("authenticated", MutationTests.NO_ERROR_MESSAGE, DisplayName = "AuthZ success and blank response when role has no delete operation restrictions.")]
        [DataRow("only-delete-role", "The mutation operation deleteEarth was successful " +
            "but the current user is unauthorized to view the response due to lack of read permissions", DisplayName = "AuthZ failure but sucessful operation where role has ONLY delete permission and NO read permission.")]
        [DataRow("wildcard-exclude-fields-role", MutationTests.NO_ERROR_MESSAGE, DisplayName = "AuthZ success and blank response for delete mutation because of reference to excluded/disallowed fields using wildcard")]
        [DataRow("only-create-role", MutationTests.USER_NOT_AUTHORIZED, DisplayName = "AuthZ failure when delete permission is NOT there.")]
        public async Task DeleteItemWithAuthPermissions(string roleName, string expectedErrorMessage)
        {
            // Create an item with "Authenticated" role
            string id = Guid.NewGuid().ToString();
            const string name = "test_name";
            string createMutation = $@"
mutation {{
    createEarth (item: {{ id: ""{id}"", name: ""{name}"" }}) {{
        id
        name
    }}
}}";

            JsonElement createResponse = await ExecuteGraphQLRequestAsync("createEarth", createMutation,
                variables: new(),
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: AuthorizationType.Authenticated.ToString()),
                clientRoleHeader: AuthorizationType.Authenticated.ToString());

            // Making sure item is created successfully
            Assert.AreEqual(id, createResponse.GetProperty("id").GetString());

            // Run mutation Update Earth;
            string mutation = @"
mutation ($id: ID!, $partitionKeyValue: String!) {
    deleteEarth (id: $id, _partitionKeyValue: $partitionKeyValue) {
        id
        name
     }
}";
            string authtoken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: roleName);
            JsonElement response = await ExecuteGraphQLRequestAsync(
                queryName: "deleteEarth",
                query: mutation,
                variables: new() { { "id", id }, { "partitionKeyValue", id } },
                authToken: authtoken,
                clientRoleHeader: roleName);

            Console.WriteLine(response.ToString());

            if (string.IsNullOrEmpty(expectedErrorMessage))
            {
                Assert.IsTrue(string.IsNullOrEmpty(response.ToString()));
            }
            else
            {
                // Validate the result contains the GraphQL authorization error code.
                string errorMessage = response.ToString();
                Assert.IsTrue(errorMessage.Contains(expectedErrorMessage));
            }
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
type Planet @model(name:""Planet"") {
    id : ID!,
    name : String,
    age : Int,
}";
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: false);
            Dictionary<string, object> dbOptions = new();
            HyphenatedNamingPolicy namingPolicy = new();

            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database)), "graphqldb");
            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container)), _containerName);
            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema)), "custom-schema.gql");
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

                    Assert.IsFalse(!queryResponse.ToString().Contains(id), "The query response was not expected to have errors. The document did not return successfully.");
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
type Planet @model(name:""Planet"") {
    id : ID!,
    name : String,
    age : Int,
}";
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: false);
            Dictionary<string, object> dbOptions = new();
            HyphenatedNamingPolicy namingPolicy = new();

            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database)), "graphqldb");
            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container)), _containerName);
            dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema)), "custom-schema.gql");
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

                    Assert.IsFalse(!mutationResponse.ToString().Contains(id), "The mutation response was not expected to have errors. The document did not create successfully.");
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
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            cosmosClient.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}

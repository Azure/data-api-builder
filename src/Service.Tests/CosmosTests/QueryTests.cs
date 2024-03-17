// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Tests.Authorization;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class QueryTests : TestBase
    {

        public static readonly string PlanetByPKQuery = @"
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
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
        endCursor
        hasNextPage
    }
}";
        public static readonly string PlanetsWithOrderBy = @"
query{
    planets (first: 10, after: null, orderBy: {id: ASC, name: null }) {
        items {
            id
            name
        },
        endCursor,
        hasNextPage
    }
}
";
        public static readonly string MoonWithInvalidAuthorizationPolicy = @"
query ($id: ID, $partitionKeyValue: String) {
    invalidAuthModel_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue){
        id
        name
    }
}";
        private static List<string> _idList;
        private const int TOTAL_ITEM_COUNT = 10;

        [TestInitialize]
        public void TestFixtureSetup()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            cosmosClient.CreateDatabaseIfNotExistsAsync(DATABASE_NAME).Wait();
            cosmosClient.GetDatabase(DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            _idList = CreateItems(DATABASE_NAME, _containerName, TOTAL_ITEM_COUNT);
        }

        [TestMethod]
        public async Task GetByPrimaryKeyWithVariables()
        {
            // Run query
            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", PlanetByPKQuery, new() { { "id", id }, { "partitionKeyValue", id } });

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        /// <summary>
        /// Tests that the GraphQLAuthorizationHandler fails requests when selected fields
        /// have a schema definition that attempts to define an authorization policy:
        /// @authorize(policy: "PolicyName")
        /// These requests should fail because defining policies on the authorize directive
        /// is not supported.
        /// </summary>
        [TestMethod]
        public async Task GetWithInvalidAuthorizationPolicyInSchema()
        {
            // Run query
            string id = _idList[0];
            string clientRoleHeader = AuthorizationType.Authenticated.ToString();
            JsonElement response = await ExecuteGraphQLRequestAsync(
                queryName: "invalidAuthModel_by_pk",
                query: MoonWithInvalidAuthorizationPolicy,
                variables: new() { { "id", id }, { "partitionKeyValue", id } },
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader),
                clientRoleHeader: clientRoleHeader);

            // Validate the result contains the GraphQL authorization error code.
            string errorMessage = response.ToString();
            Assert.IsTrue(errorMessage.Contains(AuthorizationHelpers.GRAPHQL_AUTHORIZATION_ERROR));
        }

        [TestMethod]
        public async Task GetListOfString()
        {
            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", @"
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
        tags
    }
}", new() { { "id", id }, { "partitionKeyValue", id } });

            string[] tags = response.GetProperty("tags").Deserialize<string[]>();
            Assert.AreEqual(2, tags.Length);
            CollectionAssert.AreEqual(new[] { "tag1", "tag2" }, tags);
        }

        /// <summary>
        /// Validates that a query with only __typename in the selection set
        /// returns the right type
        /// </summary>
        [TestMethod]
        public async Task QueryWithOnlyTypenameInSelectionSet()
        {
            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", @"
                query ($id: ID, $partitionKeyValue: String) {
                    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
                        __typename
                    }
                }", new() { { "id", id }, { "partitionKeyValue", id } });

            string expected = @"Planet";
            string actual = response.GetProperty("__typename").Deserialize<string>();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public async Task GetPaginatedWithVariables()
        {
            // Run paginated query
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string afterToken = null;

            do
            {
                JsonElement page = await ExecuteGraphQLRequestAsync("planets",
                    PlanetsQuery, new() { { "first", pagesize }, { "after", afterToken } });
                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
            } while (!string.IsNullOrEmpty(afterToken));

            // Validate results
            Assert.AreEqual(TOTAL_ITEM_COUNT, totalElementsFromPaginatedQuery);
        }

        [TestMethod]
        public async Task GetByPrimaryKeyWithoutVariables()
        {
            // Run query
            string id = _idList[0];
            string query = @$"
query {{
    planet_by_pk (id: ""{id}"", _partitionKeyValue: ""{id}"") {{
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
        endCursor
        hasNextPage
    }}
}}";
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", planetConnectionQueryStringFormat, variables: new());
                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
                ConvertJsonElementToStringList(page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME), pagedResponse);
            } while (!string.IsNullOrEmpty(afterToken));

            Assert.AreEqual(TOTAL_ITEM_COUNT, totalElementsFromPaginatedQuery);
        }

        [TestMethod]
        public async Task GetPaginatedWithSinglePartition()
        {
            // Run paginated query
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string afterToken = null;
            List<string> pagedResponse = new();
            string id = _idList[0];

            do
            {
                string planetConnectionQueryStringFormat = @$"
query {{
    planets (first: {pagesize}, after: {(afterToken == null ? "null" : "\"" + afterToken + "\"")},
    {QueryBuilder.FILTER_FIELD_NAME}: {{ id: {{eq: ""{id}""}} }}) {{
        items {{
            id
            name
        }}
        endCursor
        hasNextPage
    }}
}}";
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", planetConnectionQueryStringFormat, variables: new());
                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
                ConvertJsonElementToStringList(page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME), pagedResponse);
            } while (!string.IsNullOrEmpty(afterToken));

            Assert.AreEqual(1, totalElementsFromPaginatedQuery);
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
    planet_by_pk (id: ""{id}"", _partitionKeyValue: ""{id}"") {{
        id
        name
        character {{
            id
            name
        }}
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task GetWithOrderBy()
        {
            JsonElement response = await ExecuteGraphQLRequestAsync("planets", PlanetsWithOrderBy);

            int i = 0;
            // Check order matches
            foreach (string id in _idList.OrderBy(x => x))
            {
                Assert.AreEqual(id, response.GetProperty("items")[i++].GetProperty("id").GetString());
            }
        }

        /// <summary>
        /// This is to exercise the scenario when the GraphQL type and top-level entity name in the runtime config do not match.
        /// "Star" is a GraphQL type, in the runtime config, the top level entity name is "StarAlias"
        /// A match is attempted using the runtime config entity singular type name when there is no match found with the GraphQL type name.
        /// </summary>
        [TestMethod]
        public async Task GetByPrimaryKeyWhenEntityNameDoesntMatchGraphQLType()
        {
            // Run query
            // _idList is the mock data that's generated for testing purpose, arbitrarily pick the first id here to query.
            string id = _idList[0];
            string query = @$"
query {{
    planet_by_pk (id: ""{id}"", _partitionKeyValue: ""{id}"") {{
        id
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task CollectionQueryWithInlineFragmentOverlappingFields()
        {
            string query = @"
query {
    planets {
        __typename
        items {
            id
            name
            ... on Planet { id }
        }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync("planets", query);

            Assert.AreEqual(TOTAL_ITEM_COUNT, response.GetProperty("items").GetArrayLength());
        }

        [TestMethod]
        public async Task CollectionQueryWithInlineFragmentNonOverlappingFields()
        {
            string query = @"
query {
    planets {
        __typename
        items {
            name
            ... on Planet { id }
        }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync("planets", query);

            Assert.AreEqual(TOTAL_ITEM_COUNT, response.GetProperty("items").GetArrayLength());
        }

        [TestMethod]
        public async Task CollectionQueryWithFragmentOverlappingFields()
        {
            string query = @"
query {
    planets {
        __typename
        items {
            id
            name
            ... on Planet { id }
        }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync("planets", query);

            Assert.AreEqual(TOTAL_ITEM_COUNT, response.GetProperty("items").GetArrayLength());
        }

        [TestMethod]
        public async Task CollectionQueryWithFragmentNonOverlappingFields()
        {
            string query = @"
query {
    planets {
        __typename
        items {
            name
            ... p
        }
    }
}

fragment p on Planet { id }
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync("planets", query);

            Assert.AreEqual(TOTAL_ITEM_COUNT, response.GetProperty("items").GetArrayLength());
        }

        [TestMethod]
        public async Task QueryWithInlineFragmentOverlappingFields()
        {
            string query = @"
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
        __typename
        id
        name
        ... on Planet { id }
    }
}
            ";

            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query, new() { { "id", id }, { "partitionKeyValue", id } });

            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task QueryWithInlineFragmentNonOverlappingFields()
        {
            string query = @"
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
        __typename
        name
        ... on Planet { id }
    }
}
            ";

            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query, new() { { "id", id }, { "partitionKeyValue", id } });

            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task QueryWithFragmentOverlappingFields()
        {
            string query = @"
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
        __typename
        id
        name
        ... on Planet { id }
    }
}
            ";
            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query, new() { { "id", id }, { "partitionKeyValue", id } });

            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task QueryWithFragmentNonOverlappingFields()
        {
            string query = @"
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
        __typename
        name
        ... p
    }
}

fragment p on Planet { id }
            ";

            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query, new() { { "id", id }, { "partitionKeyValue", id } });

            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        /// <summary>
        /// Validates that cached data is returned for the query.
        /// First step - Query data for an ID.
        /// Second step - Modify the name field for the document queried in the first step.
        /// Third step - Query the document again to verify that cached data is returned and not modified one.
        /// </summary>
        [TestMethod]
        public async Task QueryWithCacheEnabledShouldReturnCachedResponse()
        {
            string[] args = GetArgumentsForHost();

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string id = _idList[0];

                string graphQLQuery = @$"
query {{
    planet_by_pk (id: ""{id}"") {{
        id
        name
    }}
}}";
                // First query
                JsonElement firstQueryResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                                   client,
                                   server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                   query: graphQLQuery,
                                   queryName: "planet_by_pk"
                                   );

                string name = firstQueryResponse.GetProperty("name").GetString();

                // Performing mutation
                string newName = "new_name";
                string mutation = $@"
mutation {{
    updatePlanet (id: ""{id}"", _partitionKeyValue: ""{id}"", item: {{ id: ""{id}"", name: ""{newName}"", stars: [{{ id: ""{id}"" }}] }}) {{
        id
        name
    }}
}}";

                var update = new
                {
                    id = id,
                    name = "new_name",
                };

                // Mutated name property
                JsonElement mutationResponse = await ExecuteGraphQLRequestAsync("updatePlanet", mutation, variables: new());

                // Asserting name is mutated
                Assert.IsTrue(mutationResponse.GetProperty("name").GetString().Equals(newName), "Mutation didn't change the name successfully");

                // Second query - the data returned is from the cache, not the mutated one
                JsonElement secondQueryResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                                   client,
                                   server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                   query: graphQLQuery,
                                   queryName: "planet_by_pk"
                                   );

                // Asserting cached data is returned
                Assert.IsTrue(secondQueryResponse.GetProperty("name").GetString() == name, "Query didn't return cached value");
            }
        }

        /// <summary>
        /// Validates that cached data is returned for Paginated query.
        /// First step - Execute Paginated query.
        /// Second step - Modify the name field for one of the document queried in the first step.
        /// Third step - Execute Paginated query again to verify that cached data is returned and not modified one.
        /// </summary>
        [TestMethod]
        public async Task QueryWithCacheEnabledShouldReturnCachedResponseForPaginatedQueries()
        {
            string[] args = GetArgumentsForHost();

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                // Execute the first query and hold the last item for comparison with the cached value at a later step
                JObject lastItemFromFirstQuery = await ExecutePaginatedQueryAndReturnLastPage(server, client);

                // Perform a mutation to update the name for the last returned item from the first query.
                string newName = "new_name";
                string id = lastItemFromFirstQuery.GetValue("id").ToString();

                string mutation = $@"
mutation {{
    updatePlanet (id: ""{id}"", _partitionKeyValue: ""{id}"", item: {{ id: ""{id}"", name: ""{newName}"", stars: [{{ id: ""{id}"" }}] }}) {{
        id
        name
    }}
}}";

                var update = new
                {
                    id = id,
                    name = "new_name",
                };

                // Mutated name property
                JsonElement mutationResponse = await ExecuteGraphQLRequestAsync("updatePlanet", mutation, variables: new());

                // Asserting name is mutated
                Assert.IsTrue(mutationResponse.GetProperty("name").GetString().Equals(update.name), "Mutation didn't change the name successfully");

                // Execute Second query. Response should be returned from cache and not from sdk call.
                JObject lastItemFromSecondQuery = await ExecutePaginatedQueryAndReturnLastPage(server, client);

                // Assert data returned from First query with second cached query
                Assert.IsTrue(lastItemFromFirstQuery.GetValue("id").ToString() == lastItemFromSecondQuery.GetValue("id").ToString(), "Same Page sequence not returned from cached response");
                Assert.IsTrue(lastItemFromFirstQuery.GetValue("name").ToString() == lastItemFromSecondQuery.GetValue("name").ToString(), "Cached value not returned from second request");
            }
        }

        [TestMethod]
        public async Task GraphQLQueryWithMultipleOfTheSameFieldReturnsFieldOnce()
        {
            string query = @"
query {
    planets {
        items {
            id
            id
        }
    }
}
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync("planets", query);

            Assert.AreEqual(TOTAL_ITEM_COUNT, response.GetProperty("items").GetArrayLength());
            Assert.AreEqual(_idList[0], response.GetProperty("items").EnumerateArray().First().GetProperty("id").GetString());
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

        private static async Task<JObject> ExecutePaginatedQueryAndReturnLastPage(TestServer server, HttpClient client)
        {
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string afterToken = null;
            JsonElement page;
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
        endCursor
        hasNextPage
    }}
}}";
                page = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                                   client,
                                   server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                   query: planetConnectionQueryStringFormat,
                                   queryName: "planets"
                                   );

                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
                ConvertJsonElementToStringList(page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME), pagedResponse);
            } while (!string.IsNullOrEmpty(afterToken));

            // Asserting Paginated query retured all records
            Assert.AreEqual(TOTAL_ITEM_COUNT, totalElementsFromPaginatedQuery);

            return JObject.Parse(pagedResponse.Last());
        }

        private string[] GetArgumentsForHost()
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

            EntityPermission[] permissions = new[] { new EntityPermission(Role: AuthorizationResolver.ROLE_ANONYMOUS, Actions: new[] { createAction, readAction, deleteAction }) };

            Entity entity = new(Source: new($"graphqldb.{_containerName}", null, null, null),
                                  Rest: null,
                                  GraphQL: new(Singular: "Planet", Plural: "Planets"),
                                  Permissions: permissions,
                                  Relationships: null,
                                  Mappings: null,
                                  Cache: new EntityCacheOptions()
                                  {
                                      Enabled = true,
                                      TtlSeconds = 5,
                                  }
                                  );

            string entityName = "Planet";

            // cache configuration
            RuntimeConfig configuration = ConfigurationTests.InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, entityName, new EntityCacheOptions() { Enabled = true, TtlSeconds = 5 });

            const string CUSTOM_CONFIG = "custom-config.json";
            const string CUSTOM_SCHEMA = "custom-schema.gql";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());
            File.WriteAllText(CUSTOM_SCHEMA, SCHEMA);

            return new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };
        }

        [TestCleanup]
        public void TestFixtureTearDown()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            cosmosClient.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}

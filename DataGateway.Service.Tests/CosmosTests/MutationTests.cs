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
        private static readonly string _mutationStringFormat = @"
                                                mutation ($id: ID!, $name: String!)
                                                {
                                                    addPlanet (id: $id, name: $name)
                                                    {
                                                        id
                                                        name
                                                    }
                                                }";
        private static readonly string _mutationDeleteItemStringFormat = @"
                                                mutation ($id: ID!)
                                                {
                                                    deletePlanet (id: $id)
                                                    {
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
            RegisterMutationResolver("addPlanet", DATABASE_NAME, _containerName);
            RegisterMutationResolver("deletePlanet", DATABASE_NAME, _containerName, "Delete");
        }

        [TestMethod]
        public async Task TestMutationRun()
        {
            // Run mutation Add planet;
            String id = Guid.NewGuid().ToString();
            string mutation = String.Format(_mutationStringFormat, id, "test_name");
            JsonElement response = await ExecuteGraphQLRequestAsync("addPlanet", mutation, new() { { "$id", id }, { "$name", "test_name" } });

            // Validate results
            Assert.IsFalse(response.ToString().Contains("Error"));

            // Run mutation delete item;
            mutation = String.Format(_mutationDeleteItemStringFormat, id);
            response = await ExecuteGraphQLRequestAsync("deletePlanet", mutation, new() { { "$id", id } });

            // Validate results
            Assert.IsFalse(response.ToString().Contains("Error"));
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

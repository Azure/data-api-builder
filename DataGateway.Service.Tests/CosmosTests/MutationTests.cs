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
                                                mutation
                                                {{
                                                    addPlanet (id: ""{0}"", name: ""{1}"")
                                                    {{
                                                        id
                                                        name
                                                    }}
                                                }}";

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
        }

        [TestMethod]
        public async Task TestMutationRun()
        {
            // Run mutation;
            string mutation = String.Format(_mutationStringFormat, Guid.NewGuid().ToString(), "test_name");
            JsonElement response = await ExecuteGraphQLRequestAsync("addPlanet", mutation);

            // Validate results
            Assert.IsFalse(response.ToString().Contains("Error"));
        }
    }
}

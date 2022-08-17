using System;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class OtherRestApiTests : RestApiTestBase
    {
        #region RestApiTestBase Overrides

        public override string GetQuery(string key)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
            // Setup REST Components
            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object,
                _authorizationResolver,
                _runtimeConfigProvider);
            _restController = new RestController(_restService,
                                                 _restControllerLogger);
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        /// <summary>
        /// Tests the REST Api for the correct error condition format when
        /// a DataApiBuilderException is thrown
        /// </summary>
        [TestMethod]
        public async Task RestDataApiBuilderExceptionErrorConditionFormat()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=id,content",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                exception: true,
                expectedErrorMessage: "Invalid field to be returned requested: content",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// This test verifies that when we have an unsupported opration,
        /// in this case a none operation, that we return the correct error
        /// response.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task HandleAndExecuteUnsupportedOperationUnitTestAsync()
        {
            string expected = "{\"error\":{\"code\":\"BadRequest\",\"message\":\"This operation is not supported.\",\"status\":400}}";
            // need header to instantiate identity in controller
            HeaderDictionary headers = new();
            headers.Add("x-ms-client-principal", Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"hello\":\"world\"}")));

            ConfigureRestController(_restController, string.Empty, Operation.None);

            // Setup params to invoke function with
            // Must use valid entity name
            string path = "api";
            string entityName = "Book";
            Operation operationType = Operation.None;
            string primaryKeyRoute = string.Empty;

            // Reflection to invoke a private method to unit test all code paths
            PrivateObject testObject = new(_restController);
            IActionResult actionResult = await testObject.Invoke("HandleOperation", new object[] { $"{path}/{entityName}/{primaryKeyRoute}", operationType });
            SqlTestHelper.VerifyResult(actionResult, expected, System.Net.HttpStatusCode.BadRequest, string.Empty);
        }

        #region Private helpers

        /// <summary>
        /// Helper function uses reflection to invoke
        /// private methods from outside class.
        /// Expects async method returning Task.
        /// </summary>
        class PrivateObject
        {
            private readonly object _classToInvoke;
            public PrivateObject(object classToInvoke)
            {
                _classToInvoke = classToInvoke;
            }

            public Task<IActionResult> Invoke(string privateMethodName, params object[] privateMethodArgs)
            {
                MethodInfo methodInfo = _classToInvoke.GetType().GetMethod(privateMethodName, BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (methodInfo is null)
                {
                    throw new System.Exception($"{privateMethodName} not found in class '{_classToInvoke.GetType()}'");
                }

                return (Task<IActionResult>)methodInfo.Invoke(_classToInvoke, privateMethodArgs);
            }
        }

        #endregion
    }
}

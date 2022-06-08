using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.REST
{
    /// <summary>
    /// Unit Tests for Rest components that have
    /// hard to test code paths.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RestUnitTests : SqlTestBase
    {
        private static RestController _restController;

        #region Positive Tests

        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MSSQL);

            // Setup REST Components
            RestService restService = new(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object,
                _authZResolver);
            _restController = new(restService);
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

            // Features are used to setup the httpcontext such that the test will run without null references
            IFeatureCollection features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(new HttpRequestFeature { Headers = headers });
            features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
            features.Set<IHttpResponseFeature>(new HttpResponseFeature { StatusCode = (int)HttpStatusCode.OK });
            DefaultHttpContext httpContext = new(features);

            ConfigureRestController(_restController, string.Empty);
            _restController.ControllerContext.HttpContext = httpContext;

            // Setup params to invoke function with
            // Must use valid entity name
            string entityName = "Book";
            Operation operationType = Operation.None;
            string primaryKeyRoute = string.Empty;

            // Reflection to invoke a private method to unit test all code paths
            PrivateObject testObject = new(_restController);
            IActionResult actionResult = await testObject.Invoke("HandleOperation", new object[] { entityName, operationType, primaryKeyRoute });
            SqlTestHelper.VerifyResult(actionResult, expected, System.Net.HttpStatusCode.BadRequest, string.Empty);
        }

        #endregion

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
    }
}

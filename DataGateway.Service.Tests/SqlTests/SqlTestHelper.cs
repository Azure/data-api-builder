using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    public class SqlTestHelper
    {
        public static IOptionsMonitor<RuntimeConfigPath> LoadConfig(string environment)
        {
            string configFileName = RuntimeConfigPath.GetFileNameForEnvironment(environment);

            Dictionary<string, string> configFileNameMap = new()
            {
                {
                    nameof(RuntimeConfigPath.ConfigFileName),
                    configFileName
                }
            };

            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddInMemoryCollection(configFileNameMap)
                .Build();

            RuntimeConfigPath configPath = config.Get<RuntimeConfigPath>();
            configPath.SetRuntimeConfigValue();

            return Mock.Of<IOptionsMonitor<RuntimeConfigPath>>(_ => _.CurrentValue == configPath);

        }

        /// <summary>
        /// Converts strings to JSON objects and does a deep compare
        /// </summary>
        /// <remarks>
        /// This method of comparing JSON-s provides:
        /// <list type="number">
        /// <item> Insesitivity to spaces in the JSON formatting </item>
        /// <item> Insesitivity to order for elements in dictionaries. E.g. {"a": 1, "b": 2} = {"b": 2, "a": 1} </item>
        /// <item> Sensitivity to order for elements in lists. E.g. [{"a": 1}, {"b": 2}] ~= [{"b": 2}, {"a": 1}] </item>
        /// </list>
        /// In contrast, string comparing does not provide 1 and 2.
        /// </remarks>
        /// <param name="jsonString1"></param>
        /// <param name="jsonString2"></param>
        /// <returns>True if JSON objects are the same</returns>
        public static bool JsonStringsDeepEqual(string jsonString1, string jsonString2)
        {
            return JToken.DeepEquals(JToken.Parse(jsonString1), JToken.Parse(jsonString2));
        }

        /// <summary>
        /// Adds a useful failure message around the excpeted == actual operation
        /// <summary>
        public static void PerformTestEqualJsonStrings(string expected, string actual)
        {
            Assert.IsTrue(JsonStringsDeepEqual(expected, actual),
            $"\nExpected:<{expected}>\nActual:<{actual}>");
        }

        /// <summary>
        /// Tests for different aspects of the error in a GraphQL response
        /// </summary>
        public static void TestForErrorInGraphQLResponse(string response, string message = null, string statusCode = null)
        {
            Console.WriteLine(response);

            Assert.IsTrue(response.Contains("\"errors\""), "No error was found where error is expected.");

            if (message is not null)
            {
                Console.WriteLine(response);
                Assert.IsTrue(response.Contains(message), $"Message \"{message}\" not found in error");
            }

            if (statusCode != null)
            {
                Assert.IsTrue(response.Contains($"\"code\":\"{statusCode}\""), $"Status code \"{statusCode}\" not found in error");
            }
        }

        /// <summary>
        /// Performs test on the given entity name by calling the correct Api based on the
        /// operation type passed for the given primaryKeyRoute (if any).
        /// </summary>
        /// <param name="controller">The REST controller with the request context.</param>
        /// <param name="entityName">The entity name.</param>
        /// <param name="primaryKeyRoute">The primary key portion of the route.</param>
        /// <param name="operationType">The operation type to be tested.</param>
        public static async Task<IActionResult> PerformApiTest(
            RestController controller,
            string entityName,
            string primaryKeyRoute,
            Operation operationType = Operation.Find)

        {
            IActionResult actionResult;
            switch (operationType)
            {
                case Operation.Find:
                    actionResult = await controller.Find(entityName, primaryKeyRoute);
                    break;
                case Operation.Insert:
                    actionResult = await controller.Insert(entityName);
                    break;
                case Operation.Delete:
                    actionResult = await controller.Delete(entityName, primaryKeyRoute);
                    break;
                case Operation.Update:
                case Operation.Upsert:
                    actionResult = await controller.Upsert(entityName, primaryKeyRoute);
                    break;
                case Operation.UpdateIncremental:
                case Operation.UpsertIncremental:
                    actionResult = await controller.UpsertIncremental(entityName, primaryKeyRoute);
                    break;
                default:
                    throw new NotSupportedException("This operation is not yet supported.");
            }

            return actionResult;
        }

        /// <summary>
        /// Verifies the ActionResult is as expected with the expected status code.
        /// </summary>
        /// <param name="actionResult">The action result of the operation to verify.</param>
        /// <param name="expected">string represents the expected result. This value can be null for NoContent or NotFound
        /// results of operations like GET and DELETE</param>
        /// <param name="expectedStatusCode">int represents the returned http status code</param>
        /// <param name="expectedLocationHeader">The expected location header in the response(if any).</param>
        public static void VerifyResult(
            IActionResult actionResult,
            string expected,
            HttpStatusCode expectedStatusCode,
            string expectedLocationHeader,
            bool isJson = false,
            int verifyNumRecords = -1)
        {
            JsonSerializerOptions options = new()
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string actual;
            switch (actionResult)
            {
                case OkObjectResult okResult:
                    Assert.AreEqual((int)expectedStatusCode, okResult.StatusCode);
                    actual = JsonSerializer.Serialize(okResult.Value, options);
                    // if verifyNumRecords is positive we want to compare its value to
                    // the number of elements associated with "value" in the actual result.
                    // because the okResult.Value is an annonymous type we use the serialized
                    // json string, actual, to easily get the inner array and get its length.
                    if (verifyNumRecords >= 0)
                    {
                        Dictionary<string, JsonElement[]> actualAsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement[]>>(actual);
                        Assert.AreEqual(actualAsDict["value"].Length, verifyNumRecords);
                    }

                    break;
                case CreatedResult createdResult:
                    Assert.AreEqual((int)expectedStatusCode, createdResult.StatusCode);
                    Assert.AreEqual(expectedLocationHeader, createdResult.Location);
                    actual = JsonSerializer.Serialize(createdResult.Value);
                    break;
                // NoContentResult does not have value property for messages
                case NoContentResult noContentResult:
                    Assert.AreEqual((int)expectedStatusCode, noContentResult.StatusCode);
                    actual = null;
                    break;
                case NotFoundResult notFoundResult:
                    Assert.AreEqual((int)expectedStatusCode, notFoundResult.StatusCode);
                    actual = null;
                    break;
                default:
                    JsonResult actualResult = (JsonResult)actionResult;
                    actual = JsonSerializer.Serialize(actualResult.Value, options);
                    break;
            }

            Console.WriteLine($"Expected: {expected}\nActual: {actual}");
            if (isJson && !string.IsNullOrEmpty(expected))
            {
                Assert.IsTrue(JsonStringsDeepEqual(expected, actual));
            }
            else
            {
                Assert.AreEqual(expected, actual, ignoreCase: true);
            }
        }
    }
}

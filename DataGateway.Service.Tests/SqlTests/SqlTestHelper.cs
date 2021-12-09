using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.configurations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    public class SqlTestHelper
    {
        public static IOptions<DataGatewayConfig> LoadConfig(string environment)
        {

            DataGatewayConfig datagatewayConfig = new();
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile($"appsettings.{environment}.json")
                .AddJsonFile($"appsettings.{environment}.overrides.json", optional: true)
                .Build();

            config.Bind(nameof(DataGatewayConfig), datagatewayConfig);

            return Options.Create(datagatewayConfig);
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

        public static void PerformTestEqualJsonStrings(string expected, string actual)
        {
            Assert.IsTrue(JsonStringsDeepEqual(expected, actual),
                $"\nExpected:<{expected}>\nActual:<{actual}>");
        }

        /// <summary>
        /// Performs the test by calling the given api, on the entity name,
        /// primaryKeyRoute and queryString. Uses the sql query string to get the result
        /// from database and asserts the results match.
        /// </summary>
        /// <param name="api">The REST api to be invoked.</param>
        /// <param name="entityName">The entity name.</param>
        /// <param name="primaryKeyRoute">The primary key portion of the route.</param>
        /// <param name="expectedWorker">
        /// A worker to calculate the expected sql query result. A worker is used to abstract any database
        /// specific detail from the PerformApiTest function, while still allowing the function to detect
        /// exceptions thrown during the execution of that logic.
        /// </param>
        /// <param name="expectException">True if we expect exceptions.</param>
        public static async Task PerformApiTest(
            Func<string, string, Task<IActionResult>> api,
            string entityName,
            string primaryKeyRoute,
            Task<string> expectedWorker,
            bool expectException = false)
        {

            try
            {
                IActionResult actionResult = await api(entityName, primaryKeyRoute);
                OkObjectResult okResult = (OkObjectResult)actionResult;
                JsonDocument actualJson = okResult.Value as JsonDocument;

                string expected = await expectedWorker;
                string actual = actualJson.RootElement.ToString();

                Assert.IsFalse(expectException, "An exception was suppossed to be thrown, but it was not");

                PerformTestEqualJsonStrings(expected, actual);

            }
            catch (Exception e)
            {
                // Consider scenarios:
                // no exception + expectException: true -> test fails
                // exception + expectException: true    -> test passes
                // no exception + expectException: false-> test passes
                // exception + expectException: false   -> test fails
                if (expectException && !(e is AssertFailedException))
                {
                    Assert.IsTrue(expectException);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}

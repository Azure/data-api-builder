using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests
{
    public class SqlTestHelper : TestHelper
    {
        // This is is the key which holds all the rows in the response
        // for REST requests.
        public static readonly string jsonResultTopLevelKey = "value";

        // Exception properties to put assertions when verifying results of SqlTests which expect exception.
        private const string PROPERTY_MESSAGE = "message";
        private const string PROPERTY_STATUS = "status";
        private const string PROPERTY_CODE = "code";

        public static void RemoveAllRelationshipBetweenEntities(RuntimeConfig runtimeConfig)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities.ToList())
            {
                Entity updatedEntity = new(entity.Source, entity.Rest,
                                           entity.GraphQL, entity.Permissions,
                                           Relationships: null, Mappings: entity.Mappings);
                runtimeConfig.Entities.Remove(entityName);
                runtimeConfig.Entities.Add(entityName, updatedEntity);
            }
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
            return string.IsNullOrEmpty(jsonString1) && string.IsNullOrEmpty(jsonString2) ||
                JToken.DeepEquals(JToken.Parse(jsonString1), JToken.Parse(jsonString2));
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
        public static void TestForErrorInGraphQLResponse(string response, string message = null, string statusCode = null, string path = null)
        {
            Console.WriteLine(response);

            if (message is not null)
            {
                Console.WriteLine(response);
                Assert.IsTrue(response.Contains(message), $"Message \"{message}\" not found in error");
            }

            if (statusCode != null)
            {
                Assert.IsTrue(response.Contains($"\"code\":\"{statusCode}\""), $"Status code \"{statusCode}\" not found in error");
            }

            if (path is not null)
            {
                Console.WriteLine(response);
                Assert.IsTrue(response.Contains(path), $"Path \"{path}\" not found in error");
            }
        }

        /// <summary>
        /// Verifies the ActionResult is as expected with the expected status code.
        /// </summary>
        /// <param name="expected">Expected result of the query execution.</param>
        /// <param name="request">The HttpRequestMessage sent to the engine via HttpClient.</param>
        /// <param name="response">The HttpResponseMessage returned by the engine.</param>
        /// <param name="exceptionExpected">Boolean value indicating whether an exception is expected as
        /// a result of executing the request on the engine.</param>
        /// <param name="httpMethod">The http method specified in the request.</param>
        /// <param name="expectedLocationHeader">The expected location header in the response(if any).</param>
        /// <param name="verifyNumRecords"></param>
        /// <param name="isExpectedErrorMsgSubstr">When set to true, will look for a substring 'expectedErrorMessage'
        /// in the actual exception message to verify the test result. This is helpful when the actual error message is dynamic and changes
        /// on every single run of the test.</param>
        /// <returns></returns>
        public static async Task VerifyResultAsync(
            string expected,
            HttpRequestMessage request,
            HttpResponseMessage response,
            bool exceptionExpected,
            HttpMethod httpMethod,
            string expectedLocationHeader,
            int verifyNumRecords,
            bool isExpectedErrorMsgSubstr = false)
        {
            string responseBody = await response.Content.ReadAsStringAsync();
            if (!exceptionExpected)
            {
                // Assert that the expectedLocation and actualLocation are equal in case of
                // POST operation.
                if (!string.IsNullOrEmpty(expectedLocationHeader))
                {
                    // Find the actual location using the response and request uri.
                    // Response LocalPath = Request LocalPath + "/" + actualLocationPath
                    // For eg. POST Request LocalPath: /api/Review
                    // 201 Created Response LocalPath: /api/Review/book_id/1/id/5001
                    // therefore, actualLocation = book_id/1/id/5001
                    string responseLocalPath = (response.Headers.Location.LocalPath);
                    string requestLocalPath = request.RequestUri.LocalPath;
                    string actualLocationPath = responseLocalPath.Substring(requestLocalPath.Length + 1);
                    Assert.AreEqual(expectedLocationHeader, actualLocationPath);
                }

                // Assert the number of records received is equal to expected number of records.
                if (response.StatusCode is HttpStatusCode.OK && verifyNumRecords >= 0)
                {
                    Dictionary<string, JsonElement[]> actualAsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement[]>>(responseBody);
                    Assert.AreEqual(actualAsDict[jsonResultTopLevelKey].Length, verifyNumRecords);
                }

                PerformTestEqualJsonStrings(expected, responseBody);
            }
            else
            {
                // Quote(") has to be treated differently than other unicode characters
                // as it has to be added with a preceding backslash.
                responseBody = Regex.Replace(responseBody, @"\\u0022", @"\\""");

                // Remove all carriage returns and new lines from the response body.
                responseBody = Regex.Replace(responseBody, @"\\n|\\r", "");

                // Convert the escaped characters into their unescaped form.
                responseBody = Regex.Unescape(responseBody);

                // Json Property in error which the holds the actual exception properties. 
                string PARENT_PROPERTY_ERROR = "error";

                // Generate actual and expected error JObjects to assert that they are equal.
                JsonElement expectedErrorObj = JsonDocument.Parse(expected).RootElement.GetProperty(PARENT_PROPERTY_ERROR);
                JsonElement actualErrorObj = JsonDocument.Parse(responseBody).RootElement.GetProperty(PARENT_PROPERTY_ERROR);

                // Assert that the exception subStatusCode(code) and statusCode(status) are equal.
                Assert.AreEqual(expectedErrorObj.GetProperty(PROPERTY_STATUS).ToString(),
                    actualErrorObj.GetProperty(PROPERTY_STATUS).ToString());
                Assert.AreEqual(expectedErrorObj.GetProperty(PROPERTY_CODE).ToString(),
                    actualErrorObj.GetProperty(PROPERTY_CODE).ToString());

                // Assert that the actual and expected error messages are same (if needed by the test),
                // or the expectedErrorMessage is present as a substring in the actual error message.
                string actualErrorMsg = actualErrorObj.GetProperty(PROPERTY_MESSAGE).ToString();
                string expectedErrorMsg = expectedErrorObj.GetProperty(PROPERTY_MESSAGE).ToString();
                if (isExpectedErrorMsgSubstr)
                {
                    Assert.IsTrue(actualErrorMsg.Contains(expectedErrorMsg, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    Assert.AreEqual(expectedErrorMsg, actualErrorMsg);
                }
            }
        }

        /// <summary>
        /// Helper method to get the HttpMethod based on the operation type.
        /// </summary>
        /// <param name="operationType">The operation to be executed on the entity.</param>
        /// <returns>HttpMethod representing the passed in operationType.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public static HttpMethod GetHttpMethodFromOperation(Config.Operation operationType, Config.RestMethod? restMethod = null)
        {
            switch (operationType)
            {
                case Config.Operation.Read:
                    return HttpMethod.Get;
                case Config.Operation.Insert:
                    return HttpMethod.Post;
                case Config.Operation.Delete:
                    return HttpMethod.Delete;
                case Config.Operation.Upsert:
                    return HttpMethod.Put;
                case Config.Operation.UpsertIncremental:
                    return HttpMethod.Patch;
                case Config.Operation.Execute:
                    return ConvertRestMethodToHttpMethod(restMethod);
                default:
                    throw new DataApiBuilderException(
                        message: "Operation not supported for the request.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
            }
        }

        /// <summary>
        /// Converts the provided RestMethod to the corresponding HttpMethod
        /// </summary>
        /// <param name="restMethod"></param>
        /// <returns>HttpMethod corresponding the RestMethod provided as input.</returns>
        private static HttpMethod ConvertRestMethodToHttpMethod(RestMethod? restMethod)
        {
            switch (restMethod)
            {
                case RestMethod.Get:
                    return HttpMethod.Get;
                case RestMethod.Put:
                    return HttpMethod.Put;
                case RestMethod.Patch:
                    return HttpMethod.Patch;
                case RestMethod.Delete:
                    return HttpMethod.Delete;
                case RestMethod.Post:
                default:
                    return HttpMethod.Post;
            }
        }
        /// <summary>
        /// Helper function handles the loading of the runtime config.
        /// </summary>
        public static RuntimeConfig SetupRuntimeConfig(string databaseEngine)
        {
            RuntimeConfigPath configPath = TestHelper.GetRuntimeConfigPath(databaseEngine);
            return TestHelper.GetRuntimeConfig(TestHelper.GetRuntimeConfigProvider(configPath));
        }

        /// <summary>
        /// Method to create our custom exception of type SqlException (which is a sealed class).
        /// using Reflection.
        /// </summary>
        /// <param name="number">Number to be populated in SqlException.Number</param>
        /// <param name="message">Message to be populated in SqlException.Message</param>
        /// <returns>custom SqlException</returns>
        public static SqlException CreateSqlException(int number, string message = "")
        {
            // Get all the available non-public,non-static constructors for SqlErrorCollection class.
            ConstructorInfo[] constructorsArray = typeof(SqlErrorCollection).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);

            // Invoke the only constructor to create an object of SqlErrorCollection class.
            SqlErrorCollection errors = constructorsArray[0].Invoke(null) as SqlErrorCollection;
            List<object> errorList =
                errors
                .GetType()
                .GetField("_errors", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(errors) as List<object>;

            // Get all the available non-public,non-static constructors for SqlError class.
            constructorsArray = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);

            // At this point the ConstructorInfo[] for SqlError has 2 entries: One constructor with 8 parameters,
            // and one with 9 parameters. We can choose either of them to create an object of SqlError type.
            ConstructorInfo nineParamsConstructor = constructorsArray.FirstOrDefault(c => c.GetParameters().Length == 9);

            // Create SqlError object.
            // For details on what the parameters stand for please refer:
            // https://learn.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlerror.number?view=dotnet-plat-ext-6.0#examples
            SqlError sqlError = (nineParamsConstructor
                .Invoke(new object[] { number, (byte)0, (byte)0, "", "", "", (int)0, (uint)0, null }) as SqlError)!;
            errorList.Add(sqlError);

            // Create SqlException object
            SqlException e =
                Activator.CreateInstance(
                    typeof(SqlException),
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new object[] { message, errors, null, Guid.NewGuid() },
                    null)
                as SqlException;
            return e;
        }

        /// <summary>
        /// For testing we use a JSON string that represents
        /// the runtime config that would otherwise be generated
        /// by the client for use by the runtime. This makes it
        /// easier to test with different configurations, and allows
        /// for different formats between database types.
        /// </summary>
        /// <param name="dbType"> the database type associated with this config.</param>
        /// <returns></returns>
        public static string GetRuntimeConfigJsonString(string dbType)
        {
            string magazinesSource = string.Empty;
            switch (dbType)
            {
                case TestCategory.MSSQL:
                case TestCategory.POSTGRESQL:
                    magazinesSource = "\"foo.magazines\"";
                    break;
                case TestCategory.MYSQL:
                    magazinesSource = "\"magazines\"";
                    break;
            }

            return
@"
{
  ""$schema"": ""../../project-dab/playground/dab.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": """ + dbType.ToLower() + @""",
    ""connection-string"": """"
  },
  """ + dbType.ToLower() + @""": {
    ""set-session-context"": true
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": true,
      ""path"": ""/api""
    },
    ""graphql"": {
      ""enabled"": true,
      ""path"": ""/graphql"",
      ""allow-introspection"": true
    },
    ""host"": {
      ""mode"": ""Development"",
      ""cors"": {
      ""origins"": [ ""1"", ""2"" ],
      ""allow-credentials"": true
      },
      ""authentication"": {
        ""provider"": """",
        ""jwt"": {
          ""audience"": """",
          ""issuer"": """",
          ""issuer-key"": """"
        }
      }
    }
  },
  ""entities"": {
    ""Publisher"": {
      ""source"": ""publishers"",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""read"", ""update"", ""delete"" ]
        }
      ],
      ""relationships"": {
        ""books"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""books""
        }
      }
    },
    ""Stock"": {
      ""source"": ""stocks"",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""read"", ""update"" ]
        }
      ],
      ""relationships"": {
        ""comics"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""comics"",
          ""source.fields"": [ ""categoryName"" ],
          ""target.fields"": [ ""categoryName"" ]
        }
      }
    },
    ""Book"": {
      ""source"": ""books"",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""update"", ""delete"" ]
        }
      ],
      ""relationships"": {
        ""publisher"": {
          ""cardinality"": ""one"",
          ""target.entity"": ""publisher""
        },
        ""websiteplacement"": {
          ""cardinality"": ""one"",
          ""target.entity"": ""book_website_placements""
        },
        ""reviews"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""reviews""
        },
        ""authors"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""authors"",
          ""linking.object"": ""book_author_link"",
          ""linking.source.fields"": [ ""book_id"" ],
          ""linking.target.fields"": [ ""author_id"" ]
        }
      }
    },
    ""BookWebsitePlacement"": {
      ""source"": ""book_website_placements"",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [
            ""create"",
            ""update"",
            {
              ""action"": ""delete"",
              ""policy"": {
                ""database"": ""@claims.id eq @item.id""
              },
              ""fields"": {
                ""include"": [ ""*"" ],
                ""exclude"": [ ""id"" ]
              }
            }
          ]
        }
      ],
      ""relationships"": {
          ""book_website_placements"": {
            ""cardinality"": ""one"",
            ""target.entity"": ""books""
          }
        }
      },
    ""Author"": {
      ""source"": ""authors"",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        }
      ],
      ""relationships"": {
          ""books"": {
            ""cardinality"": ""many"",
            ""target.entity"": ""books"",
            ""linking.object"": ""book_author_link""
         }
       }
     },
    ""Review"": {
      ""source"": ""reviews"",
      ""rest"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        }
      ],
      ""relationships"": {
         ""books"": {
           ""cardinality"": ""one"",
           ""target.entity"": ""books""
         }
       }
     },
    ""Magazine"": {
      ""source"": " + magazinesSource + @",
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [
             {
             ""action"": ""*"",
             ""fields"": {
               ""include"": [ ""*"" ],
               ""exclude"": [ ""issue_number"" ]
              }
            }
          ]
        }
      ]
    },
    ""Comic"": {
      ""source"": ""comics"",
      ""rest"": true,
      ""graphql"": false,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""read"", ""delete"" ]
        }
      ]
    },
    ""Broker"": {
      ""source"": ""brokers"",
      ""graphql"": false,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        }
      ]
    },
    ""WebsiteUser"": {
      ""source"": ""website_users"",
      ""rest"": false,
      ""permissions"" : []
    }
  }
}";
        }

    }
}

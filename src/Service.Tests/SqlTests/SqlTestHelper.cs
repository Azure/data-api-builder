using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests
{
    public class SqlTestHelper : TestHelper
    {
        public static void RemoveAllRelationshipBetweenEntities(RuntimeConfig runtimeConfig)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities.ToList())
            {
                Entity updatedEntity = new(entity.Source, entity.Rest,
                                           entity.GraphQL, entity.Permissions,
                                           Relationships: null, Mappings: null);
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
        /// Performs test on the given entity name by calling the correct Api based on the
        /// operation type passed for the given primaryKeyRoute (if any).
        /// </summary>
        /// <param name="controller">The REST controller with the request context.</param>
        /// <param name="entityNameOrRoute">The entity name/route.</param>
        /// <param name="primaryKeyRoute">The primary key portion of the route.</param>
        /// <param name="operationType">The operation type to be tested.</param>
        public static async Task<IActionResult> PerformApiTest(
            RestController controller,
            string path,
            string entityNameOrRoute,
            string primaryKeyRoute,
            Operation operationType = Operation.Read)

        {
            IActionResult actionResult;
            string pathAndEntityNameOrRoute = $"{path}/{entityNameOrRoute}";
            switch (operationType)
            {
                case Operation.Read:
                    actionResult = await controller.Find($"{pathAndEntityNameOrRoute}/{primaryKeyRoute}");
                    break;
                case Operation.Insert:
                    actionResult = await controller.Insert($"{pathAndEntityNameOrRoute}");
                    break;
                case Operation.Delete:
                    actionResult = await controller.Delete($"{pathAndEntityNameOrRoute}/{primaryKeyRoute}");
                    break;
                case Operation.Update:
                case Operation.Upsert:
                    actionResult = await controller.Upsert($"{pathAndEntityNameOrRoute}/{primaryKeyRoute}");
                    break;
                case Operation.UpdateIncremental:
                case Operation.UpsertIncremental:
                    actionResult = await controller.UpsertIncremental($"{pathAndEntityNameOrRoute}/{primaryKeyRoute}");
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

        /// <summary>
        /// Returns the HTTP verb for a provided Operation.
        /// </summary>
        /// <param name="operationType">Operation such as Find, Upsert, Delete, etc.
        /// When Operation.None is provided from some tests, return empty string.</param>
        /// <returns>Matching HttpConstants value</returns>
        /// <exception cref="ArgumentException"></exception>
        public static string OperationTypeToHTTPVerb(Operation operationType)
        {
            switch (operationType)
            {
                case Operation.Read:
                    return HttpConstants.GET;
                case Operation.Insert:
                    return HttpConstants.POST;
                case Operation.Upsert:
                    return HttpConstants.PUT;
                case Operation.UpsertIncremental:
                    return HttpConstants.PATCH;
                case Operation.Delete:
                    return HttpConstants.DELETE;
                case Operation.None:
                    return string.Empty;
                default:
                    throw new ArgumentException(message: $"Invalid operationType {operationType} provided");
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

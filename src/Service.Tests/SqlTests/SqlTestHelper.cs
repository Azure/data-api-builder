// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationTests;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests
{
    public static class SqlTestHelper
    {
        // This is is the key which holds all the rows in the response
        // for REST requests.
        public static readonly string jsonResultTopLevelKey = "value";

        // Exception properties to put assertions when verifying results of SqlTests which expect exception.
        private const string PROPERTY_MESSAGE = "message";
        private const string PROPERTY_STATUS = "status";
        private const string PROPERTY_CODE = "code";

        public static RuntimeConfig RemoveAllRelationshipBetweenEntities(RuntimeConfig runtimeConfig)
        {
            return runtimeConfig with
            {
                Entities = new(runtimeConfig.Entities.ToDictionary(item => item.Key, item => item.Value with { Relationships = null }))
            };
        }

        /// <summary>
        /// Converts strings to JSON objects and does a deep compare
        /// </summary>
        /// <remarks>
        /// This method of comparing JSON-s provides:
        /// <list type="number">
        /// <item> Insensitivity to spaces in the JSON formatting </item>
        /// <item> Insensitivity to order for elements in dictionaries. E.g. {"a": 1, "b": 2} = {"b": 2, "a": 1} </item>
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
        /// Compares two JSON strings for equality after converting all DateTime values if present to a consistent format.
        /// Also Adds a useful failure message around the expected == actual operation.
        /// </summary>
        /// <param name="expected">The expected JSON string.</param>
        /// <param name="actual">The actual JSON string.</param>
        public static void PerformTestEqualJsonStrings(string expected, string actual)
        {
            // If either of the strings is null or empty, no need to parse them to JToken. Assert their equality directly.
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
            {
                Assert.IsTrue(JsonStringsDeepEqual(expected, actual),
                $"\nExpected:<{expected}>\nActual:<{actual}>");
                return;
            }

            JToken expectedJObject = JToken.Parse(expected);
            JToken actualJObject = JToken.Parse(actual);

            string dateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; // ISO 8601 format

            // Function to convert different DateTime values to a consistent format
            // Example: "2021-10-01T00:00:00.000Z" and "2021-10-01T00:00:00.000+00:00" are equivalent.
            // So, we convert it to a consistent format to make the comparison easier.
            // The convertDateTime function is a local function inside the PerformTestEqualJsonStrings method.
            // It's used to encapsulate the logic for converting DateTime values to ISO 8601 format.
            // This makes the PerformTestEqualJsonStrings method easier to read and understand.
            void convertDateTimeToIsoFormat(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        foreach (JProperty prop in token.Children<JProperty>())
                        {
                            if (DateTime.TryParse(prop.Value.ToString(), out DateTime date))
                            {
                                prop.Value = date.ToString(dateTimeFormat);
                            }
                            else
                            {
                                convertDateTimeToIsoFormat(prop.Value);
                            }
                        }

                        break;
                    case JTokenType.Array:
                        foreach (JToken child in token.Children())
                        {
                            convertDateTimeToIsoFormat(child);
                        }

                        break;
                }
            }

            convertDateTimeToIsoFormat(expectedJObject);
            convertDateTimeToIsoFormat(actualJObject);

            Assert.IsTrue(JsonStringsDeepEqual(expectedJObject.ToString(), actualJObject.ToString()),
                $"\nExpected:<{expectedJObject.ToString()}>\nActual:<{actualJObject.ToString()}>");
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
        /// Helper method to preprocess response to unescape unicode characters and remove all carriage returns/new lines.
        /// </summary>
        /// <param name="response">Raw response received from the API.</param>
        /// <returns>Processed response containing unescaped unicode characters without new lines/carriage returns.</returns>
        private static string PreprocessResponse(string response)
        {
            // Quote(") has to be treated differently than other unicode characters
            // as it has to be added with a preceding backslash.
            response = Regex.Replace(response, @"\\u0022", @"\\""");

            // Remove all carriage returns and new lines from the response body.
            response = Regex.Replace(response, @"\\n|\\r", "");

            // Convert the escaped characters into their unescaped form.
            response = Regex.Unescape(response);

            return response;
        }

        /// <summary>
        /// Instantiate basic runtime config with no entity.
        /// </summary>
        /// <returns></returns>
        public static RuntimeConfig InitBasicRuntimeConfigWithNoEntity(
            DatabaseType dbType = DatabaseType.MSSQL,
            string testCategory = TestCategory.MSSQL)
        {
            DataSource dataSource = new(dbType, GetConnectionStringFromEnvironmentConfig(environment: testCategory), new());
            Config.ObjectModel.AuthenticationOptions authenticationOptions = new(Provider: nameof(EasyAuthType.StaticWebApps), null);

            RuntimeConfig runtimeConfig = new(
                Schema: "IntegrationTestMinimalSchema",
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Host: new(Cors: null, Authentication: authenticationOptions)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );

            return runtimeConfig;
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
                // Json Property in error response which holds the actual exception properties.
                string PARENT_PROPERTY_ERROR = "error";

                //Generate expected error object
                JsonElement expectedErrorObj = JsonDocument.Parse(expected).RootElement.GetProperty(PARENT_PROPERTY_ERROR);
                string expectedStatusCode = expectedErrorObj.GetProperty(PROPERTY_STATUS).ToString();
                string expectedSubStatusCode = expectedErrorObj.GetProperty(PROPERTY_CODE).ToString();
                responseBody = PreprocessResponse(responseBody);

                // Generate actual error object
                JsonElement actualErrorObj = JsonDocument.Parse(responseBody).RootElement.GetProperty(PARENT_PROPERTY_ERROR);
                string actualStatusCode = actualErrorObj.GetProperty(PROPERTY_STATUS).ToString();
                string actualSubStatusCode = actualErrorObj.GetProperty(PROPERTY_CODE).ToString();

                // Assert that the expected and actual subStatusCodes/statusCodes are equal.
                Assert.AreEqual(expectedStatusCode, actualStatusCode);
                Assert.AreEqual(expectedSubStatusCode, actualSubStatusCode);

                // Assert that the actual and expected error messages are same (if needed by the test),
                // or the expectedErrorMessage is present as a substring in the actual error message.
                string actualErrorMsg = actualErrorObj.GetProperty(PROPERTY_MESSAGE).ToString();
                string expectedErrorMsg = expectedErrorObj.GetProperty(PROPERTY_MESSAGE).ToString();
                if (isExpectedErrorMsgSubstr)
                {
                    Assert.IsTrue(actualErrorMsg.Contains(expectedErrorMsg, StringComparison.OrdinalIgnoreCase));
                    return;
                }

                Assert.AreEqual(expectedErrorMsg, actualErrorMsg);
            }
        }

        /// <summary>
        /// Helper method to get the HttpMethod based on the operation type.
        /// </summary>
        /// <param name="operationType">The operation to be executed on the entity.</param>
        /// <returns>HttpMethod representing the passed in operationType.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public static HttpMethod GetHttpMethodFromOperation(EntityActionOperation operationType, SupportedHttpVerb? restMethod = null) => operationType switch
        {
            EntityActionOperation.Read => HttpMethod.Get,
            EntityActionOperation.Insert => HttpMethod.Post,
            EntityActionOperation.Delete => HttpMethod.Delete,
            EntityActionOperation.Upsert => HttpMethod.Put,
            EntityActionOperation.UpsertIncremental => HttpMethod.Patch,
            EntityActionOperation.Execute => ConvertRestMethodToHttpMethod(restMethod),
            _ => throw new DataApiBuilderException(
                                    message: "Operation not supported for the request.",
                                    statusCode: HttpStatusCode.BadRequest,
                                    subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported),
        };

        /// <summary>
        /// Converts the provided RestMethod to the corresponding HttpMethod
        /// </summary>
        /// <param name="restMethod"></param>
        /// <returns>HttpMethod corresponding the RestMethod provided as input.</returns>
        public static HttpMethod ConvertRestMethodToHttpMethod(SupportedHttpVerb? restMethod) => restMethod switch
        {
            SupportedHttpVerb.Get => HttpMethod.Get,
            SupportedHttpVerb.Put => HttpMethod.Put,
            SupportedHttpVerb.Patch => HttpMethod.Patch,
            SupportedHttpVerb.Delete => HttpMethod.Delete,
            _ => HttpMethod.Post,
        };

        /// <summary>
        /// Helper function handles the loading of the runtime config.
        /// </summary>
        public static RuntimeConfig SetupRuntimeConfig()
        {
            FileSystemRuntimeConfigLoader configPath = TestHelper.GetRuntimeConfigLoader();
            RuntimeConfigProvider provider = new(configPath);

            return provider.GetConfig();
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
            // https://learn.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlerror.number#examples
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
        /// Helper method to construct GraphQL responses when only __typename is queried
        /// </summary>
        /// <param name="typename">A json string of the format { __typename : entity_typename }  </param>
        /// <param name="times">Number of times to repeat typename in the response</param>
        /// <returns>A string representation of an array of typename json strings</returns>
        public static string ConstructGQLTypenameResponseNTimes(string typename, int times)
        {
            StringBuilder typenameResponseBuilder = new("[");
            for (int i = 0; i < times; i++)
            {
                typenameResponseBuilder.Append(typename);
                if (i != times - 1)
                {
                    typenameResponseBuilder.Append(",");
                }

            }

            typenameResponseBuilder.Append("]");
            return typenameResponseBuilder.ToString();
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

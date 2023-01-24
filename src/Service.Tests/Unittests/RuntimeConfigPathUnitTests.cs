using System;
using System.Data;
using System.IO;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the environment variable
    /// parser for the runtime configuration. These
    /// tests verify that we parse the config correctly
    /// when replacing environment variables. Also verify
    /// we throw the right exception when environment
    /// variable names are not found.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RuntimeConfigPathUnitTests
    {
        #region Positive Tests

        /// <summary>
        /// Test valid cases for parsing the runtime config.
        /// These cases have strings close to the pattern we
        /// match when looking to replace parts of the config,
        /// strings that match said pattern, and other edge
        /// cases to reveal if the pattern matching is working.
        /// The pattern we look to match is @env('') where we take
        /// what is inside of the '', ie: @env('<match>'). The match is then
        /// used to get the associated environment variable.
        /// </summary>
        /// <param name="repKeys">Replacement used as key to get environment variable.</param>
        /// <param name="repValues">Replacement value.</param>
        [DataTestMethod]
        [DataRow(new string[] { "@env(')", "@env()", "@env(')'@env('()", "@env('@env()'", "@@eennvv((''''))" },
            new object[]
            {
                new string[] { "@env(')", "@env()", "@env(')'@env('()", "@env('@env()'", "@@eennvv((''''))" }
            },
            DisplayName = "Replacement strings that won't match.")]
        [DataRow(new string[] { "@env('envVarName')", "@env(@env('envVarName'))", "@en@env('envVarName')", "@env'()@env'@env('envVarName')')')" },
            new object[]
            {
                new string[] { "envVarValue", "@env(envVarValue)", "@enenvVarValue", "@env'()@env'envVarValue')')" }
            },
            DisplayName = "Replacement strings that match.")]
        //  since we match strings surrounded by single quotes,
        //  the following are environment variable names set to the
        //  associated values:
        // 'envVarName  -> _envVarName
        //  envVarName' ->  envVarName_
        // 'envVarName' -> _envVarName_
        [DataRow(new string[] { "@env(')", "@env()", "@env('envVarName')", "@env(''envVarName')", "@env('envVarName'')", "@env(''envVarName'')" },
            new object[]
            {
                new string[] { "@env(')", "@env()", "envVarValue", "_envVarValue", "envVarValue_", "_envVarValue_" }
            },
            DisplayName = "Replacement strings with some matches.")]
        public void CheckConfigEnvParsingTest(string[] repKeys, string[] repValues)
        {
            SetEnvVariables();
            string expectedJson = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(GetModifiedJsonString(repValues));
            string actualJson = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(GetModifiedJsonString(repKeys));
            JObject expected = JObject.Parse(expectedJson);
            JObject actual = JObject.Parse(actualJson);
            Assert.IsTrue(JToken.DeepEquals(expected, actual));
        }

        /// <summary>
        /// Method to validate that comments are allowed in config file (and are ignored during deserialization).
        /// </summary>
        [TestMethod]
        public void CheckCommentParsingInConfigFile()
        {
            string actualJson = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://dataapibuilder.azureedge.net/schemas/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                        ""options"": {
                                        // Whether we want to send user data to the underlying database.
                                            ""set-session-context"": true
                                        },
                                    ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
                                    }
                                }";
            string expectedJson = @"{
                                    ""$schema"":""https://dataapibuilder.azureedge.net/schemas/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                        ""options"": {
                                            ""set-session-context"": true
                                        },
                                    ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
                                    }
                                }";
            string expected = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(expectedJson);
            string actual = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(actualJson);
            Assert.AreEqual(expected, actual);
        }

        #endregion Positive Tests

        #region Negative Tests

        /// <summary>
        /// When we have a match that does not correspond
        /// to a valid environment variable we throw an exception.
        /// These tests verify this happens correctly.
        /// </summary>
        /// <param name="invalidEnvVarName">A match that is not a valid environment variable name.</param>
        [DataTestMethod]
        [DataRow("")]
        [DataRow("fooBARbaz")]
        // extra single quote added to environment variable
        // names to validate we don't match these
        [DataRow("''envVarName")]
        [DataRow("''envVarName'")]
        [DataRow("envVarName''")]
        [DataRow("''envVarName''")]
        public void CheckConfigEnvParsingThrowExceptions(string invalidEnvVarName)
        {
            string json = @"{ ""foo"" : ""@env('envVarName'), @env('" + invalidEnvVarName + @"')"" }";
            SetEnvVariables();
            Assert.ThrowsException<DataApiBuilderException>(() => RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(json));
        }

        [TestMethod("Validates that JSON deserialization failures are gracefully caught.")]
        public void TestDeserializationFailures()
        {
            string configJson = @"
{
    ""data-source"": {
        ""database-type"": ""notsupporteddb""
     }
}";
            Mock<ILogger<RuntimeConfigProvider>> logger = new();
            Assert.IsFalse(RuntimeConfig.TryGetDeserializedRuntimeConfig
                             (configJson,
                             out RuntimeConfig deserializedConfig,
                             logger.Object));
            Assert.IsNull(deserializedConfig);
        }

        [DataRow("", typeof(ArgumentNullException),
            "Could not determine a configuration file name that exists. (Parameter 'Configuration file name')",
            DisplayName = "Empty configuration file name.")]
        [DataRow("NonExistentConfigFile.json", typeof(FileNotFoundException),
            "Requested configuration file 'NonExistentConfigFile.json' does not exist.",
            DisplayName = "Non existent configuration file name.")]
        [TestMethod("Validates that loading of runtime config value can handle failures gracefully.")]
        public void TestLoadRuntimeConfigFailures(
            string configFileName,
            Type exceptionType,
            string exceptionMessage)
        {
            RuntimeConfigPath configPath = new()
            {
                ConfigFileName = configFileName
            };

            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            try
            {
                RuntimeConfigProvider.ConfigProviderLogger = configProviderLogger.Object;
                // This tests the logger from the constructor.
                RuntimeConfigProvider configProvider =
                    new(configPath, configProviderLogger.Object);
                RuntimeConfigProvider.LoadRuntimeConfigValue(
                    configPath,
                    out RuntimeConfig runtimeConfig);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Assert.AreEqual(exceptionType, ex.GetType());
                Assert.AreEqual(exceptionMessage, ex.Message);
                Assert.AreEqual(2, configProviderLogger.Invocations.Count);
                // This is the error logged by TryLoadRuntimeConfigValue()
                Assert.AreEqual(LogLevel.Error, configProviderLogger.Invocations[0].Arguments[0]);
                // This is the information logged by the RuntimeConfigProvider constructor.
                Assert.AreEqual(LogLevel.Information, configProviderLogger.Invocations[1].Arguments[0]);
            }
        }

        #endregion Negative Tests

        #region Helper Functions

        /// <summary>
        /// Setup some environment variables.
        /// </summary>
        private static void SetEnvVariables()
        {
            Environment.SetEnvironmentVariable($"envVarName", $"envVarValue");
            Environment.SetEnvironmentVariable($"'envVarName", $"_envVarValue");
            Environment.SetEnvironmentVariable($"envVarName'", $"envVarValue_");
            Environment.SetEnvironmentVariable($"'envVarName'", $"_envVarValue_");
        }

        /// <summary>
        /// Modify the json string with the replacements provided.
        /// This function cycles through the string array in a circular
        /// fasion.
        /// </summary>
        /// <param name="reps">Replacement strings.</param>
        /// <returns>Json string with replacements.</returns>
        public static string GetModifiedJsonString(string[] reps)
        {
            int index = 0;
            return
@"{
  ""$schema"": "".. /../project-dab/playground/dab.draft-01.schema.json"",
  ""versioning"": {
    ""version"": 1.1,
    ""patch"": 1
  },
  ""data-source"": {
    ""database-type"": """ + reps[index % reps.Length] + @""",
    ""connection-string"": ""server=dataapibuilder;database=" + reps[++index % reps.Length] + @";uid=" + reps[++index % reps.Length] + @";Password=" + reps[++index % reps.Length] + @";Allow User Variables=true"",
    ""resolver-config-file"": """ + reps[++index % reps.Length] + @"""
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": """ + reps[++index % reps.Length] + @""",
      ""path"": ""/" + reps[++index % reps.Length] + @"""
    },
    ""graphql"": {
      ""enabled"": true,
      ""path"": """ + reps[++index % reps.Length] + @""",
      ""allow-introspection"": true
    },
    ""host"": {
      ""mode"": """ + reps[++index % reps.Length] + @""",
      ""cors"": {
        ""origins"": [ """ + reps[++index % reps.Length] + @""", """ + reps[++index % reps.Length] + @""" ],
        ""allow-credentials"": """ + reps[++index % reps.Length] + @"""
      },
      ""authentication"": {
        ""provider"": """ + reps[++index % reps.Length] + @""",
        ""jwt"": {
                ""audience"": """",
          ""issuer"": """",
          ""issuer-key"": """ + reps[++index % reps.Length] + @"""
        }
      }
    }
  },
  ""entities"": {
    ""Publisher"": {
      ""source"": """ + reps[++index % reps.Length] + @"." + reps[++index % reps.Length] + @""",
      ""rest"": """ + reps[++index % reps.Length] + @""",
      ""graphql"": """ + reps[++index % reps.Length] + @""",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", """ + reps[++index % reps.Length] + @""", ""update"", ""delete"" ]
        }
      ],
      ""relationships"": {
        ""books"": {
          ""cardinality"": ""many"",
          ""target.entity"": """ + reps[++index % reps.Length] + @"""
        }
      }
    },
    ""Stock"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": null,
      ""graphql"": """ + reps[++index % reps.Length] + @""",
      ""permissions"": [
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""", ""read"", ""update"" ]
        }
      ],
      ""relationships"": {
        ""comics"": {
          ""cardinality"": ""many"",
          ""target.entity"": """ + reps[++index % reps.Length] + @""",
          ""source.fields"": [ ""categoryName"" ],
          ""target.fields"": [ """ + reps[++index % reps.Length] + @""" ]
        }
      }
    },
    ""Book"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""", ""update"", """ + reps[++index % reps.Length] + @""" ]
        }
      ],
      ""relationships"": {
        ""publishers"": {
          ""cardinality"": """ + reps[++index % reps.Length] + @""",
          ""target.entity"": """ + reps[++index % reps.Length] + @"""
        },
        ""websiteplacement"": {
          ""cardinality"": ""one"",
          ""target.entity"": """ + reps[++index % reps.Length] + @"""
        },
        ""reviews"": {
          ""cardinality"": ""many"",
          ""target.entity"": """ + reps[++index % reps.Length] + @"""
        },
        ""authors"": {
          ""cardinality"": """ + reps[++index % reps.Length] + @""",
          ""target.entity"": ""Author"",
          ""linking.object"": ""book_author_link"",
          ""linking.source.fields"": [ ""book_id"" ],
          ""linking.target.fields"": [ """ + reps[++index % reps.Length] + @""" ]
        }
      },
      ""mappings"": {
        ""id"": """ + reps[++index % reps.Length] + @""",
        ""title"": """ + reps[++index % reps.Length] + @"""
      }
    },
    ""BookWebsitePlacement"": {
      ""source"": ""book_website_placements"",
      ""rest"": """ + reps[++index % reps.Length] + @""",
      ""graphql"": """ + reps[++index % reps.Length] + @""",
      ""permissions"": [
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        },
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [
            """ + reps[++index % reps.Length] + @""",
            """ + reps[++index % reps.Length] + @""",
            {
              ""action"": ""delete"",
              ""policy"": {
                ""database"": ""@claims.id eq @item.id""
              },
              ""fields"": {
                ""include"": [ ""*"" ],
                ""exclude"": [ """ + reps[++index % reps.Length] + @""" ]
              }
            }
          ]
        }
      ],
      ""relationships"": {
        ""books"": {
          ""cardinality"": ""one"",
          ""target.entity"": """ + reps[++index % reps.Length] + @"""
        }
      }
    },
    ""Author"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": true,
      ""graphql"": """ + reps[++index % reps.Length] + @""",
      ""permissions"": [
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ ""read"" ]
        }
      ],
      ""relationships"": {
        ""books"": {
          ""cardinality"": ""many"",
          ""target.entity"": """ + reps[++index % reps.Length] + @""",
          ""linking.object"": ""book_author_link""
        }
      }
    },
    ""Review"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        }
      ],
      ""relationships"": {
        ""books"": {
          ""cardinality"": """ + reps[++index % reps.Length] + @""",
          ""target.entity"": """ + reps[++index % reps.Length] + @"""
        }
      }
    },
    ""Comic"": {
      ""source"": ""comics"",
      ""rest"": true,
      ""graphql"": null,
      ""permissions"": [
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ null ]
        },
        {
            ""role"": ""authenticated"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""", ""read"", """ + reps[++index % reps.Length] + @""" ]
        }
      ]
    },
    ""Broker"": {
      ""source"": ""brokers"",
      ""graphql"": false,
      ""permissions"": [
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        }
      ]
    },
    ""WebsiteUser"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": false,
      ""permissions"": []
    },
    ""SupportedType"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": false,
      ""permissions"": []
    },
    ""stocks_price"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": """ + reps[++index % reps.Length] + @""",
      ""permissions"": []
    },
    ""Tree"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": """ + reps[++index % reps.Length] + @""",
      ""permissions"": [
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ ""create"", """ + reps[++index % reps.Length] + @""", ""update"", ""delete"" ]
        }
      ],
      ""mappings"": {
        ""species"": ""Scientific Name"",
        ""region"": ""United State's " + reps[++index % reps.Length] + @"""
      }
    },
    ""Shrub"": {
      ""source"": ""trees"",
      ""rest"": true,
      ""permissions"": [
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ ""create"", ""read"", """ + reps[++index % reps.Length] + @""", ""delete"" ]
        }
      ],
      ""mappings"": {
        ""species"": """ + reps[++index % reps.Length] + @"""
      }
    },
    ""Fungus"": {
      ""source"": ""fungi"",
      ""rest"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""", ""read"", """ + reps[++index % reps.Length] + @""", ""delete"" ]
        }
      ],
      ""mappings"": {
        ""spores"": ""hazards""
      }
    },
    ""books_view_all"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        },
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ ""read"" ]
        }
      ],
      ""relationships"": {
        }
    },
    ""stocks_view_selected"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": """ + reps[++index % reps.Length] + @""",
          ""actions"": [ ""read"" ]
        }
      ],
      ""relationships"": {
      }
    },
    ""books_publishers_view_composite"": {
      ""source"": """ + reps[++index % reps.Length] + @""",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ """ + reps[++index % reps.Length] + @""" ]
        }
      ],
      ""relationships"": {
      }
    }
  }
}
";
        }

        #endregion Helper Functions
    }
}

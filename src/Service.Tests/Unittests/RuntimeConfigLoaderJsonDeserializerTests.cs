// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
    public class RuntimeConfigLoaderJsonDeserializerTests
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
        [DataRow(
            new string[] { "@env(')", "@env()", "@env(')'@env('()", "@env('@env()'", "@@eennvv((''''))" },
            new string[] { "@env(')", "@env()", "@env(')'@env('()", "@env('@env()'", "@@eennvv((''''))" },
            DisplayName = "Replacement strings that won't match.")]
        [DataRow(
            new string[] { "@env('envVarName')", "@env(@env('envVarName'))", "@en@env('envVarName')", "@env'()@env'@env('envVarName')')')" },
            new string[] { "envVarValue", "@env(envVarValue)", "@enenvVarValue", "@env'()@env'envVarValue')')" },
            DisplayName = "Replacement strings that match.")]
        //  since we match strings surrounded by single quotes,
        //  the following are environment variable names set to the
        //  associated values:
        // 'envVarName  -> _envVarName
        //  envVarName' ->  envVarName_
        // 'envVarName' -> _envVarName_
        [DataRow(
            new string[] { "@env(')", "@env()", "@env('envVarName')", "@env(''envVarName')", "@env('envVarName'')", "@env(''envVarName'')" },
            new string[] { "@env(')", "@env()", "envVarValue", "_envVarValue", "envVarValue_", "_envVarValue_" },
            DisplayName = "Replacement strings with some matches.")]
        public void CheckConfigEnvParsingTest(string[] repKeys, string[] repValues)
        {
            SetEnvVariables();
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(GetModifiedJsonString(repValues), out RuntimeConfig expectedConfig), "Should read the expected config");
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(GetModifiedJsonString(repKeys), out RuntimeConfig actualConfig), "Should read actual config");

            Assert.AreEqual(expectedConfig.ToJson(), actualConfig.ToJson());
        }

        /// <summary>
        /// Method to validate that comments are skipped in config file (and are ignored during deserialization).
        /// </summary>
        [TestMethod]
        public void CheckCommentParsingInConfigFile()
        {
            string actualJson = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                        ""options"": {
                                        // Whether we want to send user data to the underlying database.
                                            ""set-session-context"": true
                                        },
                                    ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
                                    }
                                }";
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(actualJson, out RuntimeConfig _), "Should not fail to parse with comments");
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
            StringJsonConverterFactory stringConverterFactory = new();
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            options.Converters.Add(stringConverterFactory);
            Assert.ThrowsException<DataApiBuilderException>(() => JsonSerializer.Deserialize<StubJsonType>(json, options));
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
            Assert.IsFalse(RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig));
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
            MockFileSystem fileSystem = new();
            RuntimeConfigLoader loader = new(fileSystem);

            Assert.IsFalse(loader.TryLoadConfig(configFileName, out RuntimeConfig _));
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
        /// fashion.
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
    ""database-type"": ""mssql"",
    ""connection-string"": ""server=dataapibuilder;database=" + reps[++index % reps.Length] + @";uid=" + reps[++index % reps.Length] + @";Password=" + reps[++index % reps.Length] + @";"",
    ""resolver-config-file"": """ + reps[++index % reps.Length] + @"""
  },
  ""runtime"": {
    ""rest"": {
      ""path"": ""/" + reps[++index % reps.Length] + @"""
    },
    ""graphql"": {
      ""enabled"": true,
      ""path"": """ + reps[++index % reps.Length] + @""",
      ""allow-introspection"": true
    },
    ""host"": {
      ""mode"": ""development"",
      ""cors"": {
        ""origins"": [ """ + reps[++index % reps.Length] + @""", """ + reps[++index % reps.Length] + @""" ],
        ""allow-credentials"": true
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
          ""actions"": [ ""*"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""update"", ""delete"" ]
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
          ""actions"": [ ""*"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""read"", ""update"" ]
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
    }
  }
}
";
        }

        #endregion Helper Functions

        record StubJsonType(string Foo);
    }
}

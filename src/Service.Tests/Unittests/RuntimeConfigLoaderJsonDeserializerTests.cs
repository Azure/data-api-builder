// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for deserializing the runtime configuration. These
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
            true,
            true,
            DisplayName = "Replacement strings that won't match.")]
        [DataRow(
            new string[] { "@env('envVarName')", "@env(@env('envVarName'))", "@en@env('envVarName')", "@env'()@env'@env('envVarName')')')" },
            new string[] { "envVarValue", "@env(envVarValue)", "@enenvVarValue", "@env'()@env'envVarValue')')" },
            false,
            true,
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
            false,
            true,
            DisplayName = "Replacement strings with some matches.")]
        [DataRow(
            new string[] { "@env('envVarName')", "@env(@env('envVarName'))", "@en@env('envVarName')", "@env'()@env'@env('envVarName')')')" },
            new string[] { "envVarValue", "@env(envVarValue)", "@enenvVarValue", "@env'()@env'envVarValue')')" },
            false,
            false,
            DisplayName = "Replacement strings that match, but shouldn't be replaced.")]
        public void CheckConfigEnvParsingTest(
            string[] repKeys,
            string[] repValues,
            bool exceptionThrown,
            bool replaceEnvVar)
        {
            SetEnvVariables();
            try
            {
                RuntimeConfig expectedConfig;
                if (replaceEnvVar)
                {
                    Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(
                        GetModifiedJsonString(repValues, @"""postgresql"""), out expectedConfig, replaceEnvVar: replaceEnvVar),
                        "Should read the expected config");
                }
                else
                {
                    Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(
                        GetModifiedJsonString(repKeys, @"""postgresql"""), out expectedConfig, replaceEnvVar: replaceEnvVar),
                        "Should read the expected config");
                }

                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(
                    GetModifiedJsonString(repKeys, @"""@env('enumVarName')"""), out RuntimeConfig actualConfig, replaceEnvVar: replaceEnvVar),
                    "Should read actual config");
                Assert.AreEqual(expectedConfig.ToJson(), actualConfig.ToJson());
            }
            catch (Exception ex)
            {
                Assert.IsTrue(exceptionThrown);
                Assert.AreEqual("A valid Connection String should be provided.", ex.Message);
            }
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
                                    },
                                    ""entities"":{ }
                                }";
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(actualJson, out RuntimeConfig _), "Should not fail to parse with comments");
        }

        /// <summary>
        /// Test to validate that optional properties
        /// are nullable, don't contain defaults on serialization
        /// but have the effect of default values when deserialized.
        /// It starts with a minimal config and incrementally
        /// adds the optional subproperties. At each step, tests for valid deserialization.
        [TestMethod]
        public void TestNullableOptionalProps()
        {
            // Test with no runtime property
            StringBuilder minJson = new(@"
                                ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""@env('test-connection-string')""
                                    },
                                ""entities"": { }");

            TryParseAndAssertOnDefaults("{" + minJson + "}", out _);

            // Test with an empty runtime property
            minJson.Append(@", ""runtime"": ");
            string emptyRuntime = minJson + "{ }}";
            TryParseAndAssertOnDefaults("{" + emptyRuntime, out _);

            // Test with empty sub properties of runtime
            minJson.Append(@"{ ""rest"": { }, ""graphql"": { },
                            ""base-route"" : """",");
            StringBuilder minJsonWithHostSubProps = new(minJson + @"""telemetry"" : { }, ""host"" : ");
            StringBuilder minJsonWithTelemetrySubProps = new(minJson + @"""host"" : { }, ""telemetry"" : ");

            string emptyRuntimeSubProps = minJsonWithHostSubProps + "{ } } }";
            TryParseAndAssertOnDefaults("{" + emptyRuntimeSubProps, out _);

            // Test with empty host sub-properties
            minJsonWithHostSubProps.Append(@"{ ""cors"": { }, ""authentication"": { } } }");
            string emptyHostSubProps = minJsonWithHostSubProps + "}";
            TryParseAndAssertOnDefaults("{" + emptyHostSubProps, out _);

            // Test with empty telemetry sub-properties
            minJsonWithTelemetrySubProps.Append(@"{ ""application-insights"": { } } }");

            string emptyTelemetrySubProps = minJsonWithTelemetrySubProps + "}";
            TryParseAndAssertOnDefaults("{" + emptyTelemetrySubProps, out _);
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
            StringJsonConverterFactory stringConverterFactory = new(EnvironmentVariableReplacementFailureMode.Throw);
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            options.Converters.Add(stringConverterFactory);
            Assert.ThrowsException<DataApiBuilderException>(() => JsonSerializer.Deserialize<StubJsonType>(json, options));
        }

        [DataRow("\"notsupporteddb\"", "",
            DisplayName = "Tests that a database type which will not deserialize correctly fails.")]
        [DataRow("\"mssql\"", "\"notsupportedconnectionstring\"",
            DisplayName = "Tests that a malformed connection string fails during post-processing.")]
        [TestMethod("Validates that JSON deserialization failures are gracefully caught.")]
        public void TestDataSourceDeserializationFailures(string dbType, string connectionString)
        {
            string configJson = @"
{
    ""data-source"": {
        ""database-type"": " + dbType + @",
        ""connection-string"": " + connectionString + @"
    },
    ""entities"":{ }
}";
            // replaceEnvVar: true is needed to make sure we do post-processing for the connection string case
            Assert.IsFalse(RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig, replaceEnvVar: true));
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
            FileSystemRuntimeConfigLoader loader = new(fileSystem);

            Assert.IsFalse(loader.TryLoadConfig(configFileName, out RuntimeConfig _));
        }

        /// <summary>
        /// Method to validate that FilenotFoundexception is thrown if sub-data source file is not found.
        /// </summary>
        [TestMethod]
        public void TestLoadRuntimeConfigSubFilesFails()
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
    
                                    },
                                    ""data-source-files"":[""FileNotFound.json""],
                                    ""entities"":{ }
                                }";
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(actualJson, out RuntimeConfig runtimeConfig), "Should parse the data-source-files correctly.");
            Assert.IsTrue(runtimeConfig.ListAllDataSources().Count() == 1);
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
            Environment.SetEnvironmentVariable($"enumVarName", $"postgresql");
        }

        /// <summary>
        /// Modify the json string with the replacements provided.
        /// This function cycles through the string array in a circular
        /// fashion.
        /// </summary>
        /// <param name="reps">Replacement strings.</param>
        /// <param name="enumString">Replacement string to use for a test enum.</param>
        /// <returns>Json string with replacements.</returns>
        public static string GetModifiedJsonString(string[] reps, string enumString)
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
    ""database-type"": " + enumString + @",
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

        private static bool TryParseAndAssertOnDefaults(string json, out RuntimeConfig parsedConfig)
        {
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(json, out parsedConfig));
            Assert.AreEqual(RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK, parsedConfig.Schema);
            Assert.IsTrue(parsedConfig.IsRestEnabled);
            Assert.AreEqual(RestRuntimeOptions.DEFAULT_PATH, parsedConfig.RestPath);
            Assert.IsTrue(parsedConfig.IsGraphQLEnabled);
            Assert.AreEqual(GraphQLRuntimeOptions.DEFAULT_PATH, parsedConfig.GraphQLPath);
            Assert.IsTrue(parsedConfig.AllowIntrospection);
            Assert.IsFalse(parsedConfig.IsDevelopmentMode());
            Assert.IsTrue(parsedConfig.IsStaticWebAppsIdentityProvider);
            Assert.IsTrue(parsedConfig.IsRequestBodyStrict);
            Assert.IsTrue(parsedConfig.Runtime?.Telemetry?.ApplicationInsights is null
                || !parsedConfig.Runtime.Telemetry.ApplicationInsights.Enabled);
            return true;
        }

        #endregion Helper Functions

        record StubJsonType(string Foo);
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

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
                        GetModifiedJsonString(repValues, @"""postgresql"""), out expectedConfig, replacementSettings: new DeserializationVariableReplacementSettings(azureKeyVaultOptions: null, doReplaceEnvVar: replaceEnvVar, doReplaceAkvVar: false)),
                        "Should read the expected config");
                }
                else
                {
                    Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(
                        GetModifiedJsonString(repKeys, @"""postgresql"""), out expectedConfig, replacementSettings: new DeserializationVariableReplacementSettings(azureKeyVaultOptions: null, doReplaceEnvVar: replaceEnvVar, doReplaceAkvVar: false)),
                        "Should read the expected config");
                }

                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(
                    GetModifiedJsonString(repKeys, @"""@env('enumVarName')"""), out RuntimeConfig actualConfig, replacementSettings: new DeserializationVariableReplacementSettings(azureKeyVaultOptions: null, doReplaceEnvVar: replaceEnvVar, doReplaceAkvVar: false)),
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
        /// Tests the parsing of the runtime configuration with environment variables based on replaceEnvVar parameter.
        /// Verifies the below five different cases:
        /// 1. datasource options correctly deserializes the boolean value,
        /// 2. datasource options correctly deserializes the string value,
        /// 3. when datasource options is empty, it is deserialized as an empty dictionary
        /// 4. when datasource options is null, it is correctly deserialized as null.
        /// 5. when datasource options is not given, it is correctly deserialized as null.
        /// MySQL, PgSql, and DwSql db types are unaffected by replaceEnvVar because those db's don't support options.
        /// </summary>
        /// <param name="replaceEnvVar">A boolean indicating whether to replace environment variables in the configuration.</param>
        [DataTestMethod]
        [DataRow(false, "mssql", DisplayName = "Do not replace environment variables containing boolean values.")]
        [DataRow(true, "mssql", DisplayName = "Replace environment variables containing boolean values.")]
        [DataRow(false, "cosmosdb_nosql", DisplayName = "Do not replace environment variables containing string values.")]
        [DataRow(true, "cosmosdb_nosql", DisplayName = "Replace environment variables containing string values.")]
        [DataRow(false, "mysql", DisplayName = "Do not replace environment variables when datasource option is empty.")]
        [DataRow(true, "mysql", DisplayName = "Replace environment variables when datasource option is empty.")]
        [DataRow(false, "postgresql", DisplayName = "Do not replace environment variables when datasource option is null.")]
        [DataRow(true, "postgresql", DisplayName = "Replace environment variables when datasource option is null.")]
        [DataRow(false, "dwsql", DisplayName = "Do not replace environment variables when datasource option is not given.")]
        [DataRow(true, "dwsql", DisplayName = "Replace environment variables when datasource option is not given.")]
        public void TestConfigParsingWithEnvVarReplacement(bool replaceEnvVar, string databaseType)
        {
            // Arrange
            SetEnvironmentVariablesFromDictionary(_environmentFileContentDict);

            string configWithEnvVar = _configWithVariableDataSource.Replace("{0}", GetDataSourceConfigForGivenDatabase(databaseType));
            bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
              configWithEnvVar, out RuntimeConfig runtimeConfig, replacementSettings: new DeserializationVariableReplacementSettings(azureKeyVaultOptions: null, doReplaceEnvVar: replaceEnvVar, doReplaceAkvVar: true));

            // Assert
            Assert.IsTrue(isParsingSuccessful);
            switch (databaseType)
            {
                case "mssql":
                    Assert.AreEqual(runtimeConfig.DataSource.DatabaseType, DatabaseType.MSSQL);
                    Assert.AreEqual(runtimeConfig.DataSource.Options["set-session-context"].ToString().ToLower(), GetExpectedPropertyValue("MSSQL_SET_SESSION_CONTEXT", replaceEnvVar).ToLower());
                    break;
                case "cosmosdb_nosql":
                    Assert.AreEqual(runtimeConfig.DataSource.DatabaseType, DatabaseType.CosmosDB_NoSQL);
                    Assert.AreEqual(runtimeConfig.DataSource.Options["database"].ToString(), GetExpectedPropertyValue("DATABASE_NAME", replaceEnvVar));
                    Assert.AreEqual(runtimeConfig.DataSource.Options["container"].ToString(), GetExpectedPropertyValue("DATABASE_CONTAINER", replaceEnvVar));
                    Assert.AreEqual(runtimeConfig.DataSource.Options["schema"].ToString(), GetExpectedPropertyValue("GRAPHQL_SCHEMA_PATH", replaceEnvVar));
                    break;
                case "mysql":
                    Assert.AreEqual(runtimeConfig.DataSource.DatabaseType, DatabaseType.MySQL);
                    Assert.AreEqual(runtimeConfig.DataSource.Options.Count, 0);
                    break;
                case "postgresql":
                    Assert.AreEqual(runtimeConfig.DataSource.DatabaseType, DatabaseType.PostgreSQL);
                    Assert.AreEqual(runtimeConfig.DataSource.Options, null);
                    break;
                case "dwsql":
                    Assert.AreEqual(runtimeConfig.DataSource.DatabaseType, DatabaseType.DWSQL);
                    Assert.AreEqual(runtimeConfig.DataSource.Options, null);
                    break;
            }

            // Cleanup
            ClearEnvironmentVariablesFromDictionary(_environmentFileContentDict);
        }

        /// <summary>
        /// Test the parsing of DataSource section in runtime config for cosmosdb_nosql
        /// where under cosmosDb options has database as null, container is not provided, and schema is an empty string.
        /// It verifies that the given config is correctly deserialized according to the given values, and invalidity or absence of values are
        /// handled by CosmosDBMetadataProvider seperately during initialization.
        /// </summary>
        [TestMethod]
        public void TestConfigParsingWhenDataSourceOptionsForCosmosDBContainsInvalidValues()
        {
            // Act
            SetEnvironmentVariablesFromDictionary(_environmentFileContentDict);

            string configWithEnvVar = _configWithVariableDataSource.Replace("{0}", GetDataSourceOptionsForCosmosDBWithInvalidValues());
            bool isParsingSuccessful = RuntimeConfigLoader.TryParseConfig(
              configWithEnvVar, out RuntimeConfig runtimeConfig, replacementSettings: new DeserializationVariableReplacementSettings(azureKeyVaultOptions: null, doReplaceEnvVar: true, doReplaceAkvVar: true));

            // Assert
            Assert.IsTrue(isParsingSuccessful);
            Assert.AreEqual(runtimeConfig.DataSource.DatabaseType, DatabaseType.CosmosDB_NoSQL);
            Assert.IsNull(runtimeConfig.DataSource.Options["database"]);
            Assert.IsFalse(runtimeConfig.DataSource.Options.ContainsKey("container"));
            Assert.AreEqual(runtimeConfig.DataSource.Options["schema"].ToString(), "");

            // Cleanup
            ClearEnvironmentVariablesFromDictionary(_environmentFileContentDict);
        }

        /// <summary>
        /// Retrieves the value of an environment variable if replacement is enabled, otherwise returns a placeholder string.
        /// </summary>
        /// <param name="envVarName">The name of the environment variable.</param>
        /// <param name="replaceEnvVar">A boolean indicating whether to replace the environment variable with its value.</param>
        /// <returns>
        /// If replacement is enabled, the value of the environment variable.
        /// Otherwise, a placeholder string in the format "@env('variableName')".
        /// </returns>
        private static string GetExpectedPropertyValue(string envVarName, bool replaceEnvVar)
        {
            if (replaceEnvVar)
            {
                return Environment.GetEnvironmentVariable(envVarName);
            }
            else
            {
                return $"@env('{envVarName}')";
            }
        }

        /// <summary>
        /// Test method to validate that environment variable replacement works correctly
        /// for the telemetry.application-insights.enabled property when set through config
        /// or through environment variables
        /// </summary>
        [TestMethod]
        [DataRow(true, DisplayName = "ApplicationInsights.Enabled set to true (literal bool)")]
        [DataRow(false, DisplayName = "ApplicationInsights.Enabled set to false (literal bool)")]
        public void TestTelemetryApplicationInsightsEnabled(bool expected)
        {
            TestTelemetryApplicationInsightsEnabledInternal(expected.ToString().ToLower(), expected);
        }

        [TestMethod]
        [DataRow("true", true, DisplayName = "ApplicationInsights.Enabled from string 'true'")]
        [DataRow("false", false, DisplayName = "ApplicationInsights.Enabled from string 'false'")]
        [DataRow("1", true, DisplayName = "ApplicationInsights.Enabled from string '1'")]
        [DataRow("0", false, DisplayName = "ApplicationInsights.Enabled from string '0'")]
        public void TestTelemetryApplicationInsightsEnabledFromString(string configSetting, bool expected)
        {

            TestTelemetryApplicationInsightsEnabledInternal($"\"{configSetting}\"", expected);
        }

        [TestMethod]
        [DataRow("true", true, DisplayName = "ApplicationInsights.Enabled from environment 'true'")]
        [DataRow("false", false, DisplayName = "ApplicationInsights.Enabled from environment 'false'")]
        [DataRow("1", true, DisplayName = "ApplicationInsights.Enabled from environment '1'")]
        [DataRow("0", false, DisplayName = "ApplicationInsights.Enabled from environment '0'")]
        public void TestTelemetryApplicationInsightsEnabledFromEnvironment(string configSetting, bool expected)
        {
            // Arrange
            const string envVarName = "APP_INSIGHTS_ENABLED";
            string envVarValue = configSetting;
            // Set up the environment variable
            Environment.SetEnvironmentVariable(envVarName, envVarValue);

            try
            {
                TestTelemetryApplicationInsightsEnabledInternal("\"@env('APP_INSIGHTS_ENABLED')\"", expected);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(envVarName, null);
            }

        }
        public static void TestTelemetryApplicationInsightsEnabledInternal(string configValue, bool expected)
        {
            const string AppInsightsConnectionString = "InstrumentationKey=test-key";

            string configJson = @"{
                    ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                    ""data-source"": {
                        ""database-type"": ""mssql"",
                        ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
                    },
                    ""runtime"": {
                        ""telemetry"": {
                            ""application-insights"": {
                                ""enabled"": " + configValue + @",
                                ""connection-string"": """ + AppInsightsConnectionString + @"""
                            }
                        }
                    },
                    ""entities"": { }
                }";

            // Act
            bool IsParsed = RuntimeConfigLoader.TryParseConfig(
                configJson,
                out RuntimeConfig runtimeConfig,
                replacementSettings: new DeserializationVariableReplacementSettings(
                    azureKeyVaultOptions: null,
                    doReplaceEnvVar: true,
                    doReplaceAkvVar: false));

            // Assert
            Assert.IsTrue(IsParsed);
            Assert.AreEqual(AppInsightsConnectionString, runtimeConfig.Runtime.Telemetry.ApplicationInsights.ConnectionString, "Connection string should be preserved");
            Assert.AreEqual(expected, runtimeConfig.Runtime.Telemetry.ApplicationInsights.Enabled, "ApplicationInsights enabled value should match expected value");
        }

        /// <summary>
        ///  
        /// </summary>
        /// <param name="configValue">Value to set in the config to cause error</param>
        /// <param name="message">Error message</param>
        [TestMethod]
        [DataRow("somenonboolean", "Invalid boolean value: somenonboolean. Specify either true or 1 for true, false or 0 for false", DisplayName = "ApplicationInsights.Enabled invalid value should error")]
        public void TestTelemetryApplicationInsightsEnabledShouldError(string configValue, string message)
        {
            string configJson = @"{
                    ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                    ""data-source"": {
                        ""database-type"": ""mssql"",
                        ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
                    },
                    ""runtime"": {
                        ""telemetry"": {
                            ""application-insights"": {
                                ""enabled"": """ + configValue + @""",
                                ""connection-string"": ""InstrumentationKey=test-key""
                            }
                        }
                    },
                    ""entities"": { }
                }";

            // Arrange
            Mock<ILogger> mockLogger = new();

            // Act
            bool isParsed = RuntimeConfigLoader.TryParseConfig(
                configJson,
                out RuntimeConfig runtimeConfig,
                replacementSettings: new DeserializationVariableReplacementSettings(
                    azureKeyVaultOptions: null,
                    doReplaceEnvVar: true,
                    doReplaceAkvVar: false),
                logger: mockLogger.Object);

            // Assert
            Assert.IsFalse(isParsed);
            Assert.IsNull(runtimeConfig);

            Assert.AreEqual(1, mockLogger.Invocations.Count, "Should raise 1 exception");
            Assert.AreEqual(5, mockLogger.Invocations[0].Arguments.Count, "Log should have 4 arguments");
            JsonException ConfigException = mockLogger.Invocations[0].Arguments[3] as JsonException;
            Assert.IsInstanceOfType(ConfigException, typeof(JsonException), "Should have raised a Json Exception");
            Assert.AreEqual(message, ConfigException.Message);
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
        /// </summary>
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
            minJson.Append(@"{ ""rest"": { }, ""graphql"": { }, ""mcp"": { },
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
            minJsonWithTelemetrySubProps.Append(@"{ ""application-insights"": { }, ""log-level"": { }, ""open-telemetry"": { }, ""azure-log-analytics"": { }, ""file"": { } } }");

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
            StringJsonConverterFactory stringConverterFactory = new(new(doReplaceEnvVar: true, envFailureMode: EnvironmentVariableReplacementFailureMode.Throw));
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
            Assert.IsFalse(RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig, replacementSettings: new DeserializationVariableReplacementSettings(azureKeyVaultOptions: null, doReplaceEnvVar: true, doReplaceAkvVar: true)));
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

            // Use null replacement settings for this test
            Assert.IsFalse(loader.TryLoadConfig(configFileName, out RuntimeConfig _, replacementSettings: null));
        }

        /// <summary>
        /// Method to validate that FileNotFoundException is thrown if sub-data source file is not found.
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
    ""connection-string"": ""server=dataapibuilder;database=" + reps[++index % reps.Length] + @";uid=" + reps[++index % reps.Length] + @";Password=" + reps[++index % reps.Length] + @";""
  },
  ""runtime"": {
    ""rest"": {
      ""path"": ""/" + reps[++index % reps.Length] + @"""
    },
    ""graphql"": {
      ""enabled"": true,
      ""path"": """ + reps[++index % reps.Length] + @""",
      ""allow-introspection"": true,
      ""multiple-mutations"": {
        ""create"": {
            ""enabled"": false
        }
      }
    },
    ""mcp"": {
      ""enabled"": true,
      ""path"": """ + reps[++index % reps.Length] + @"""
    },
    ""host"": {
      ""mode"": ""development"",
      ""cors"": {
        ""origins"": [ """ + reps[++index % reps.Length] + @""", """ + reps[++index % reps.Length] + @"""],
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

        /// <summary>
        /// Config containing data-source property.
        /// </summary>
        private static string _configWithVariableDataSource = @"
            {
              ""$schema"": ""/dab-schema.json"",
              ""data-source"": {0},
              ""runtime"": {
                ""rest"": {
                  ""enabled"": true,
                  ""path"": ""/api""
                },
                ""graphql"": {
                  ""allow-introspection"": true,
                  ""enabled"": true,
                  ""path"": ""/graphql""
                },
                ""mcp"": {
                  ""enabled"": true,
                  ""path"": ""/mcp""
                },
                ""host"": {
                  ""mode"": ""development"",
                  ""cors"": {
                    ""origins"": [],
                    ""allow-credentials"": false
                  },
                  ""authentication"": {
                    ""provider"": ""EntraID""
                  }
                }
              },
              ""entities"": {}
            }
            ";

        /// <summary>
        /// Returns a datasource option for a given database type.
        /// MSSQL options has a boolean value, CosmosDB options has string value,
        /// MySQL datasource option is empty, PostgreSQL datasource option is null,
        /// and DWSQL has no options.
        /// The role of deserializer is just to correctly translate the values from the config.
        /// This test is checking that we support different ways in which options can be provided.
        /// </summary>
        private static string GetDataSourceConfigForGivenDatabase(string databaseType)
        {
            string options = "";
            string databaseTypeEnvVariable = "";
            string connectionStringEnvVarName = "DATABASE_CONNECTION_STRING";

            switch (databaseType)
            {
                case "mssql":
                    databaseTypeEnvVariable = $"@env('MSSQL_DB_TYPE')";
                    options = @",""options"": { ""set-session-context"": ""@env('MSSQL_SET_SESSION_CONTEXT')"" }";
                    break;
                case "cosmosdb_nosql":
                    databaseTypeEnvVariable = $"@env('COSMOS_DB_TYPE')";
                    options = @",
                        ""options"": {
                            ""database"": ""@env('DATABASE_NAME')"",
                            ""container"": ""@env('DATABASE_CONTAINER')"",
                            ""schema"": ""@env('GRAPHQL_SCHEMA_PATH')""
                          }";
                    break;
                case "mysql":
                    databaseTypeEnvVariable = $"@env('MYSQL_DB_TYPE')";
                    options = @",""options"": {}";
                    break;
                case "postgresql":
                    databaseTypeEnvVariable = $"@env('POSTGRESQL_DB_TYPE')";
                    connectionStringEnvVarName = "DATABASE_CONNECTION_STRING_PGSQL";
                    options = @",""options"": null";
                    break;
                case "dwsql":
                    databaseTypeEnvVariable = $"@env('DWSQL_DB_TYPE')";
                    options = "";
                    break;
            }

            return $@"
            {{
                ""database-type"": ""{databaseTypeEnvVariable}"",
                ""connection-string"": ""@env('{connectionStringEnvVarName}')""
                {options}
            }}";
        }

        /// <summary>
        /// Gets the data source options for CosmosDB with no container, null database, and empty schema.
        /// This is to test that the missing or null values are correctly deserialized.
        /// The invalid values will be handled by CosmosDBMetadataProvider during initialization.
        /// </summary>
        private static string GetDataSourceOptionsForCosmosDBWithInvalidValues()
        {
            return $@"
            {{
                ""database-type"": ""@env('COSMOS_DB_TYPE')"",
                ""connection-string"": ""@env('DATABASE_CONNECTION_STRING')"",
                ""options"": {{
                    ""database"": null,
                    ""schema"": """"
                }}
            }}";
        }

        /// <summary>
        /// Clears the environment variables defined in the provided dictionary.
        /// </summary>
        /// <param name="environmentFileContentDict">A dictionary containing environment variables that needs to be cleared.</param>
        private static void ClearEnvironmentVariablesFromDictionary(Dictionary<string, string> environmentFileContentDict)
        {
            foreach (KeyValuePair<string, string> entry in environmentFileContentDict)
            {
                Environment.SetEnvironmentVariable(entry.Key, null);
            }
        }

        /// <summary>
        /// A dictionary representing environment variables for testing environment variable replacement in the config.
        /// </summary>
        private static Dictionary<string, string> _environmentFileContentDict = new()
        {
            { "COSMOS_DB_TYPE", "cosmosdb_nosql" },
            { "MSSQL_DB_TYPE", "mssql" },
            { "MYSQL_DB_TYPE", "mysql" },
            { "POSTGRESQL_DB_TYPE", "postgresql" },
            { "DWSQL_DB_TYPE", "dwsql" },
            { "MSSQL_SET_SESSION_CONTEXT", "true" },
            { "DATABASE_CONTAINER", "xyz"},
            { "DATABASE_NAME", "planet" },
            { "GRAPHQL_SCHEMA_PATH", "gql-schema.gql" },
            { "DATABASE_CONNECTION_STRING", "Data Source=<>;Initial Catalog=<>;User ID=<>;Password=<>;" },
            { "DATABASE_CONNECTION_STRING_PGSQL", "Host=<>;Database=<>;username=<>;password=<>" }
        };

        /// <summary>
        /// Sets environment variables from a given dictionary.
        /// </summary>
        /// <param name="environmentFileContentDict">A dictionary containing environment variables.</param>
        private static void SetEnvironmentVariablesFromDictionary(Dictionary<string, string> environmentFileContentDict)
        {
            foreach (KeyValuePair<string, string> entry in environmentFileContentDict)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }

        private static bool TryParseAndAssertOnDefaults(string json, out RuntimeConfig parsedConfig)
        {
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(json, out parsedConfig));
            Assert.AreEqual(RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK, parsedConfig.Schema);
            Assert.IsTrue(parsedConfig.IsRestEnabled);
            Assert.AreEqual(RestRuntimeOptions.DEFAULT_PATH, parsedConfig.RestPath);
            Assert.IsTrue(parsedConfig.IsGraphQLEnabled);
            Assert.AreEqual(GraphQLRuntimeOptions.DEFAULT_PATH, parsedConfig.GraphQLPath);
            Assert.IsTrue(parsedConfig.IsMcpEnabled);
            Assert.AreEqual(McpRuntimeOptions.DEFAULT_PATH, parsedConfig.McpPath);
            Assert.IsTrue(parsedConfig.AllowIntrospection);
            Assert.IsFalse(parsedConfig.IsDevelopmentMode());
            Assert.IsTrue(parsedConfig.IsAppServiceIdentityProvider);
            Assert.IsTrue(parsedConfig.IsRequestBodyStrict);
            Assert.IsTrue(parsedConfig.IsLogLevelNull());
            Assert.IsTrue(parsedConfig.Runtime?.Telemetry?.ApplicationInsights is null
                || !parsedConfig.Runtime.Telemetry.ApplicationInsights.Enabled);
            Assert.IsTrue(parsedConfig.Runtime?.Telemetry?.OpenTelemetry is null
                || !parsedConfig.Runtime.Telemetry.OpenTelemetry.Enabled);
            Assert.IsTrue(parsedConfig.Runtime?.Telemetry?.AzureLogAnalytics is null
                || !parsedConfig.Runtime.Telemetry.AzureLogAnalytics.Enabled);
            Assert.IsTrue(parsedConfig.Runtime?.Telemetry?.File is null
                || !parsedConfig.Runtime.Telemetry.File.Enabled);
            return true;
        }

        #endregion Helper Functions

        record StubJsonType(string Foo);

        /// <summary>
        /// Test to verify Azure Key Vault variable replacement from local .akv file.
        /// </summary>
        [TestMethod]
        public void TestAkvVariableReplacementFromLocalFile()
        {
            // Arrange: create a temporary .akv secrets file
            string akvFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".akv");
            string secretConnectionString = "Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;";
            File.WriteAllText(akvFilePath, $"DBCONN={secretConnectionString}\nAPI_KEY=abcd\n# Comment line should be ignored\n MALFORMEDLINE \n");

            // Escape backslashes for JSON
            string escapedPath = akvFilePath.Replace("\\", "\\\\");

            string jsonConfig = $$"""
            {
              "$schema": "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json",
              "data-source": {
                "database-type": "mssql",
                "connection-string": "@akv('DBCONN')"
              },
              "azure-key-vault": {
                "endpoint": "{{escapedPath}}"
              },
              "entities": { }
            }
            """;

            try
            {
                // Act
                DeserializationVariableReplacementSettings replacementSettings = new(
                    azureKeyVaultOptions: null,
                    doReplaceEnvVar: false,
                    doReplaceAkvVar: true);
                bool parsed = RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig config, replacementSettings: replacementSettings);

                // Assert
                Assert.IsTrue(parsed, "Config should parse successfully with local AKV file replacement.");
                Assert.IsNotNull(config, "Config should not be null.");
                Assert.AreEqual(secretConnectionString, config.DataSource.ConnectionString, "Connection string should be replaced from AKV local file secret.");
            }
            finally
            {
                // Cleanup
                if (File.Exists(akvFilePath))
                {
                    File.Delete(akvFilePath);
                }
            }
        }

        /// <summary>
        /// Validates that when an AKV secret's value itself contains an @env('...') pattern, it is NOT further resolved
        /// because replacement only runs once per original JSON token. Demonstrates that nested env patterns inside
        /// AKV secret values are left intact.
        /// </summary>
        [TestMethod]
        public void TestAkvSecretValueContainingEnvPatternIsNotEnvExpanded()
        {
            string akvFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".akv");
            // Valid MSSQL connection string which embeds an @env('env') pattern in the Database value.
            // This pattern should NOT be expanded because replacement only runs once on the original JSON token (@akv('DBCONN')).
            string secretValueWithEnvPattern = "Server=localhost;Database=@env('env');User Id=sa;Password=XXXX;";
            File.WriteAllText(akvFilePath, $"DBCONN={secretValueWithEnvPattern}\n");
            string escapedPath = akvFilePath.Replace("\\", "\\\\");

            // Set env variable to prove it would be different if expansion occurred.
            Environment.SetEnvironmentVariable("env", "SHOULD_NOT_APPEAR");

            string jsonConfig = $$"""
            {
              "$schema": "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json",
              "data-source": {
                "database-type": "mssql",
                "connection-string": "@akv('DBCONN')"
              },
              "azure-key-vault": {
                "endpoint": "{{escapedPath}}"
              },
              "entities": { }
            }
            """;

            try
            {
                DeserializationVariableReplacementSettings replacementSettings = new(
                    azureKeyVaultOptions: null,
                    doReplaceEnvVar: true,
                    doReplaceAkvVar: true);
                bool parsed = RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig config, replacementSettings: replacementSettings);
                Assert.IsTrue(parsed, "Config should parse successfully.");
                Assert.IsNotNull(config);

                string actual = config.DataSource.ConnectionString;
                Assert.IsTrue(actual.Contains("@env('env')"), "Nested @env pattern inside AKV secret should remain unexpanded.");
                Assert.IsFalse(actual.Contains("SHOULD_NOT_APPEAR"), "Env var value should not be expanded inside AKV secret.");
                Assert.IsTrue(actual.Contains("Application Name="), "Application Name should be appended for MSSQL when env replacement is enabled.");

                SqlConnectionStringBuilder builderOriginal = new(secretValueWithEnvPattern.Replace("Server=", "Data Source=").Replace("Database=", "Initial Catalog="));
                SqlConnectionStringBuilder builderActual = new(actual);
                Assert.AreEqual(builderOriginal["Data Source"], builderActual["Data Source"], "Server/Data Source should match.");
                Assert.AreEqual(builderOriginal["Initial Catalog"], builderActual["Initial Catalog"], "Database/Initial Catalog should match (with env pattern retained).");
                Assert.AreEqual(builderOriginal["User ID"], builderActual["User ID"], "User Id should match.");
                Assert.AreEqual(builderOriginal["Password"], builderActual["Password"], "Password should match.");
            }
            finally
            {
                if (File.Exists(akvFilePath))
                {
                    File.Delete(akvFilePath);
                }

                Environment.SetEnvironmentVariable("env", null);
            }
        }

        /// <summary>
        /// Validates two-pass replacement where an env var resolves to an AKV pattern which then resolves to the secret value.
        /// connection-string = @env('env_variable'), env_variable value = @akv('DBCONN'), AKV secret DBCONN holds the final connection string.
        /// </summary>
        [TestMethod]
        public void TestEnvVariableResolvingToAkvPatternIsExpandedInSecondPass()
        {
            string akvFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".akv");
            string finalSecretValue = "Server=localhost;Database=Test;User Id=sa;Password=XXXX;";
            File.WriteAllText(akvFilePath, $"DBCONN={finalSecretValue}\n");
            string escapedPath = akvFilePath.Replace("\\", "\\\\");
            Environment.SetEnvironmentVariable("env_variable", "@akv('DBCONN')");

            string jsonConfig = $$"""
            {
              "$schema": "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json",
              "data-source": {
                "database-type": "mssql",
                "connection-string": "@env('env_variable')"
              },
              "azure-key-vault": {
                "endpoint": "{{escapedPath}}"
              },
              "entities": { }
            }
            """;

            try
            {
                DeserializationVariableReplacementSettings replacementSettings = new(
                    azureKeyVaultOptions: null,
                    doReplaceEnvVar: true,
                    doReplaceAkvVar: true);
                bool parsed = RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig config, replacementSettings: replacementSettings);
                Assert.IsTrue(parsed, "Config should parse successfully.");
                Assert.IsNotNull(config);

                string expected = RuntimeConfigLoader.GetConnectionStringWithApplicationName(finalSecretValue);
                SqlConnectionStringBuilder builderExpected = new(expected);
                SqlConnectionStringBuilder builderActual = new(config.DataSource.ConnectionString);
                Assert.AreEqual(builderExpected["Data Source"], builderActual["Data Source"], "Data Source should match.");
                Assert.AreEqual(builderExpected["Initial Catalog"], builderActual["Initial Catalog"], "Initial Catalog should match.");
                Assert.AreEqual(builderExpected["User ID"], builderActual["User ID"], "User ID should match.");
                Assert.AreEqual(builderExpected["Password"], builderActual["Password"], "Password should match.");
                Assert.IsTrue(builderActual.ApplicationName?.Contains("dab_"), "Application Name should be appended including product identifier.");
            }
            finally
            {
                if (File.Exists(akvFilePath))
                {
                    File.Delete(akvFilePath);
                }

                Environment.SetEnvironmentVariable("env_variable", null);
            }
        }
    }
}

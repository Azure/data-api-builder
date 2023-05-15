// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Tests for Utils methods.
    /// </summary>
    [TestClass]
    public class UtilsTests
    {
        /// <summary>
        /// Setup the logger for CLI
        /// </summary>
        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            TestHelper.SetupTestLoggerForCLI();
        }

        /// <summary>
        /// Test to validate the REST Path constructed from the input entered using
        /// --rest option  
        /// </summary>
        /// <param name="restRoute">REST Route input from the --rest option</param>
        /// <param name="expectedRestPath">Expected REST path to be constructed</param>
        [DataTestMethod]
        [DataRow(null, null, DisplayName = "No Rest Path definition")]
        [DataRow("true", true, DisplayName = "REST enabled for the entity")]
        [DataRow("false", false, DisplayName = "REST disabled for the entity")]
        [DataRow("customPath", "/customPath", DisplayName = "Custom REST path defined for the entity")]
        public void TestContructRestPathDetails(string? restRoute, object? expectedRestPath)
        {
            object? actualRestPathDetails = ConstructRestPathDetails(restRoute);
            Assert.AreEqual(expectedRestPath, actualRestPathDetails);
        }

        /// <summary>
        /// Test to validate the GraphQL Type constructed from the input entered using
        /// --graphql option
        /// </summary>
        /// <param name="graphQLType">GraphQL Type input from --graphql option</param>
        /// <param name="expectedGraphQLType">Expected GraphQL Type to be constructed</param>
        [DataTestMethod]
        [DataRow(null, null, false, DisplayName = "No GraphQL Type definition")]
        [DataRow("true", true, false, DisplayName = "GraphQL enabled for the entity")]
        [DataRow("false", false, false, DisplayName = "GraphQL disabled for the entity")]
        [DataRow("book", null, true, DisplayName = "Custom GraphQL type - Singular value defined")]
        [DataRow("book:books", null, true, DisplayName = "Custom GraphQL type - Singular and Plural values defined")]
        public void TestConstructGraphQLTypeDetails(string? graphQLType, object? expectedGraphQLType, bool isSingularPluralType)
        {
            object? actualGraphQLType = ConstructGraphQLTypeDetails(graphQLType);
            if (!isSingularPluralType)
            {
                Assert.AreEqual(expectedGraphQLType, actualGraphQLType);
            }
            else
            {
                SingularPlural expectedType = new(Singular: "book", Plural: "books");
                Assert.AreEqual(expectedType, actualGraphQLType);
            }

        }

        /// <summary>
        /// Test to check the precedence logic for config file in CLI
        /// </summary>
        [DataTestMethod]
        [DataRow("", "my-config.json", "my-config.json", DisplayName = "user provided the config file and environment variable was not set.")]
        [DataRow("Test", "my-config.json", "my-config.json", DisplayName = "user provided the config file and environment variable was set.")]
        [DataRow("Test", null, $"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}", DisplayName = "config not provided, but environment variable was set.")]
        [DataRow("", null, $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}", DisplayName = "neither config was provided, nor environment variable was set.")]
        public void TestConfigSelectionBasedOnCliPrecedence(
            string? environmentValue,
            string? userProvidedConfigFile,
            string expectedRuntimeConfigFile)
        {
            if (!File.Exists(expectedRuntimeConfigFile))
            {
                File.Create(expectedRuntimeConfigFile).Dispose();
            }

            string? envValueBeforeTest = Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME);
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
            Assert.IsTrue(TryGetConfigFileBasedOnCliPrecedence(userProvidedConfigFile, out string actualRuntimeConfigFile));
            Assert.AreEqual(expectedRuntimeConfigFile, actualRuntimeConfigFile);
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, envValueBeforeTest);
        }

        /// <summary>
        /// Test to verify negative/positive string numerals are correctly parsed as integers
        /// Decimal values are parsed as double.
        /// Boolean string is correctly parsed as boolean
        /// everything else is parsed as string.
        /// </summary>
        [TestMethod]
        public void TestTryParseSourceParameterDictionary()
        {
            IEnumerable<string>? parametersList = new string[] { "param1:123", "param2:-243", "param3:220.12", "param4:True", "param5:dab" };
            Assert.IsTrue(TryParseSourceParameterDictionary(parametersList, out Dictionary<string, object>? sourceParameters));
            Assert.IsNotNull(sourceParameters);
            Assert.AreEqual(sourceParameters.GetValueOrDefault("param1"), 123);
            Assert.AreEqual(sourceParameters.GetValueOrDefault("param2"), -243);
            Assert.AreEqual(sourceParameters.GetValueOrDefault("param3"), 220.12);
            Assert.AreEqual(sourceParameters.GetValueOrDefault("param4"), true);
            Assert.AreEqual(sourceParameters.GetValueOrDefault("param5"), "dab");
        }

        /// <summary>
        /// Validates permissions operations are valid for the provided source type.
        /// </summary>
        /// <param name="operations">CRUD + Execute + *</param>
        /// <param name="sourceType">Table, StoredProcedure, View</param>
        /// <param name="isSuccess">True/False</param>
        [DataTestMethod]
        [DataRow(new string[] { "*" }, SourceType.StoredProcedure, true, DisplayName = "PASS: Stored-Procedure with wildcard CRUD operation.")]
        [DataRow(new string[] { "execute" }, SourceType.StoredProcedure, true, DisplayName = "PASS: Stored-Procedure with execute operation only.")]
        [DataRow(new string[] { "create", "read" }, SourceType.StoredProcedure, false, DisplayName = "FAIL: Stored-Procedure with more than 1 CRUD operation.")]
        [DataRow(new string[] { "*" }, SourceType.Table, true, DisplayName = "PASS: Table with wildcard CRUD operation.")]
        [DataRow(new string[] { "create" }, SourceType.Table, true, DisplayName = "PASS: Table with 1 CRUD operation.")]
        [DataRow(new string[] { "create", "read" }, SourceType.Table, true, DisplayName = "PASS: Table with more than 1 CRUD operation.")]
        [DataRow(new string[] { "*" }, SourceType.View, true, DisplayName = "PASS: View with wildcard CRUD operation.")]
        [DataRow(new string[] { "create" }, SourceType.View, true, DisplayName = "PASS: View with 1 CRUD operation.")]
        [DataRow(new string[] { "create", "read" }, SourceType.View, true, DisplayName = "PASS: View with more than 1 CRUD operation.")]

        public void TestStoredProcedurePermissions(
            string[] operations,
            SourceType sourceType,
            bool isSuccess)
        {
            Assert.AreEqual(isSuccess, Utils.VerifyOperations(operations, sourceType));
        }

        /// <summary>
        /// Test to verify correct conversion of operation string name to operation type name.
        /// </summary>
        [DataTestMethod]
        [DataRow("*", Operation.All, true, DisplayName = "PASS: Correct conversion of wildcard operation")]
        [DataRow("create", Operation.Create, true, DisplayName = "PASS: Correct conversion of CRUD operation")]
        [DataRow(null, Operation.None, false, DisplayName = "FAIL: Invalid operation null.")]

        public void TestConversionOfOperationStringNameToOperationTypeName(
            string? operationStringName,
            Operation expectedOperationTypeName,
            bool isSuccess)
        {
            Assert.AreEqual(isSuccess, Utils.TryConvertOperationNameToOperation(operationStringName, out Operation operationTypeName));
            if (isSuccess)
            {
                Assert.AreEqual(operationTypeName, expectedOperationTypeName);
            }
        }

        /// <summary>
        /// Test to verify that CLI is able to figure out if the api path prefix for rest/graphql contains invalid characters.
        /// </summary>
        [DataTestMethod]
        [DataRow("/", ApiType.REST, true, DisplayName = "Only forward slash as api path")]
        [DataRow("/$%^", ApiType.REST, false, DisplayName = "Api path containing only reserved characters.")]
        [DataRow("/rest-api", ApiType.REST, true, DisplayName = "Valid api path")]
        [DataRow("/graphql@api", ApiType.GraphQL, false, DisplayName = "Api path containing some reserved characters.")]
        [DataRow("/api path", ApiType.REST, true, DisplayName = "Api path containing space.")]
        public void TestApiPathIsWellFormed(string apiPath, ApiType apiType, bool expectSuccess)
        {
            Assert.AreEqual(expectSuccess, IsApiPathValid(apiPath, apiType));
        }

        /// <summary>
        /// Test to verify that both Audience and Issuer is mandatory when Authentication Provider is 
        /// neither EasyAuthType or Simulator. If Authentication Provider is either EasyAuth or Simulator
        /// audience and issuer are ignored.
        /// </summary>
        [DataTestMethod]
        [DataRow("StaticWebApps", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: Audience and Issuer ignored with StaticWebApps.")]
        [DataRow("StaticWebApps", null, "issuer-xxx", true, DisplayName = "PASS: Issuer ignored with StaticWebApps.")]
        [DataRow("StaticWebApps", "aud-xxx", null, true, DisplayName = "PASS: Audience ignored with StaticWebApps.")]
        [DataRow("StaticWebApps", null, null, true, DisplayName = "PASS: StaticWebApps correctly configured with neither audience nor issuer.")]
        [DataRow("AppService", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: Audience and Issuer ignored with AppService.")]
        [DataRow("AppService", null, "issuer-xxx", true, DisplayName = "PASS: Issuer ignored with AppService.")]
        [DataRow("AppService", "aud-xxx", null, true, DisplayName = "PASS: Audience ignored with AppService.")]
        [DataRow("AppService", null, null, true, DisplayName = "PASS: AppService correctly configured with neither audience nor issuer.")]
        [DataRow("Simulator", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: Audience and Issuer ignored with Simulator.")]
        [DataRow("Simulator", null, "issuer-xxx", true, DisplayName = "PASS: Issuer ignored with Simulator.")]
        [DataRow("Simulator", "aud-xxx", null, true, DisplayName = "PASS: Audience ignored with Simulator.")]
        [DataRow("Simulator", null, null, true, DisplayName = "PASS: Simulator correctly configured with neither audience nor issuer.")]
        [DataRow("AzureAD", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: AzureAD correctly configured with both audience and issuer.")]
        [DataRow("AzureAD", null, "issuer-xxx", false, DisplayName = "FAIL: AzureAD incorrectly configured with no audience specified.")]
        [DataRow("AzureAD", "aud-xxx", null, false, DisplayName = "FAIL: AzureAD incorrectly configured with no issuer specified.")]
        [DataRow("AzureAD", null, null, false, DisplayName = "FAIL: AzureAD incorrectly configured with no audience or issuer specified.")]
        public void TestValidateAudienceAndIssuerForAuthenticationProvider(
            string authenticationProvider,
            string? audience,
            string? issuer,
            bool expectSuccess)
        {
            Assert.AreEqual(
                expectSuccess,
                ValidateAudienceAndIssuerForJwtProvider(authenticationProvider, audience, issuer)
            );
        }

        /// <summary>
        /// Test to verify that when DAB_ENVIRONMENT variable is set, also base config and 
        /// dab-config.{DAB_ENVIRONMENT}.json file is present, then when DAB engine is started, it will merge
        /// the two config and use the merged config to startup the engine.
        /// Here, baseConfig(dab-config.json) has no connection_string, while dab-config.Test.json has a defined connection string.
        /// once the `dab start` is executed the merge happens and the merged file contains the connection string from the
        /// Test config.
        /// Scenarios Covered:
        /// 1. Merging of Array: Complete override of Book permissions from the second config (environment based config).
        /// 2. Merging Property when present in both config: Connection string in the second config overrides that of the first.
        /// 3. Non-merging when a property in the environmentConfig file is null: {data-source.options} is null in the environment config,
        /// So it is added to the merged config as it is with no change.
        /// 4. Merging when a property is only present in the environmentConfig file: Publisher entity is present only in environment config,
        /// So it is directly added to the merged config. 
        /// 5. Properties of same name but different level do not conflict: source is both a entityName and a property inside book entity, both are
        /// treated differently.
        /// </summary>
        [TestMethod]
        public void TestMergeConfig()
        {
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, "Test");
            File.WriteAllText("dab-config.json", BASE_CONFIG);
            File.WriteAllText("dab-config.Test.json", ENV_BASED_CONFIG);
            if (TryMergeConfigsIfAvailable(out string mergedConfig))
            {
                Assert.AreEqual(mergedConfig, "dab-config.Test.merged.json");
                Assert.IsTrue(File.Exists(mergedConfig));
                Assert.IsTrue(JToken.DeepEquals(JObject.Parse(MERGED_CONFIG), JObject.Parse(File.ReadAllText(mergedConfig))));
            }
            else
            {
                Assert.Fail("Failed to merge config files.");
            }
        }

        /// <summary>
        /// Test to verify that merged config file is only used for the below scenario
        /// 1. Environment value is set.
        /// 2. Both Base and envBased config file is present.
        /// In all other cases, the mergedConfigFile method should return empty string for the mergedConfigFile.
        /// </summary>
        [DataTestMethod]
        [DataRow("", false, false, "", false, DisplayName = "If environment value is not set, merged config file is not generated.")]
        [DataRow("", false, true, "", false, DisplayName = "If environment value is not set, merged config file is not generated.")]
        [DataRow("", true, false, "", false, DisplayName = "If environment value is not set, merged config file is not generated.")]
        [DataRow("", true, true, "", false, DisplayName = "If environment value is not set, merged config file is not generated.")]
        [DataRow("Test", false, false, "", false, DisplayName = "Environment value set but base config not available, merged config file is not generated.")]
        [DataRow("Test", false, true, "", false, DisplayName = "Environment value set but base config not available, merged config file is not generated.")]
        [DataRow("Test", true, false, "", false, DisplayName = "Environment value set but env based config not available, merged config file is not generated.")]
        [DataRow("Test", true, true, "dab-config.Test.merged.json", true, DisplayName = "environment value set and both base and envConfig available, merged config file is generated.")]
        public void TestMergeConfigAvailability(
            string environmentValue,
            bool isBaseConfigPresent,
            bool isEnvironmentBasedConfigPresent,
            string expectedMergedConfigFileName,
            bool expectedIsMergedConfigAvailable)
        {
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
            string baseConfig = "dab-config.json";
            string envBasedConfig = "dab-config.Test.json";
            if (File.Exists(baseConfig))
            {
                File.Delete(baseConfig);
            }

            if (File.Exists(envBasedConfig))
            {
                File.Delete(envBasedConfig);
            }

            if (isBaseConfigPresent)
            {
                if (!File.Exists(baseConfig))
                {
                    File.Create(baseConfig).Close();
                    File.WriteAllText(baseConfig, "{}");
                }
            }

            if (isEnvironmentBasedConfigPresent)
            {
                if (!File.Exists(envBasedConfig))
                {
                    File.Create(envBasedConfig).Close();
                    File.WriteAllText(envBasedConfig, "{}");
                }
            }

            Assert.AreEqual(expectedIsMergedConfigAvailable, TryMergeConfigsIfAvailable(out string mergedConfigFile));
            Assert.AreEqual(expectedMergedConfigFileName, mergedConfigFile);
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            if (File.Exists($"{CONFIGFILE_NAME}{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}{CONFIG_EXTENSION}");
            }

            if (File.Exists($"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}");
            }

            if (File.Exists("my-config.json"))
            {
                File.Delete("my-config.json");
            }

            if (File.Exists($"{CONFIGFILE_NAME}.Test.merged{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.Test.merged{CONFIG_EXTENSION}");
            }
        }
    }
}

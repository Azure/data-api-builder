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
        [TestInitialize]
        public void SetupLoggerForCLI()
        {
            Mock<ILogger<Utils>> utilsLogger = new();
            Utils.SetCliUtilsLogger(utilsLogger.Object);
        }

        /// <summary>
        /// Test to validate the successful construction of REST object for 
        /// various possible REST Path and Methods.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, null, typeof(bool), DisplayName = "REST Configuration - Rest Enabled without Methods")]
        [DataRow(false, null, typeof(bool), DisplayName = "REST Configuration - Rest Disabled")]
        [DataRow("book", null, typeof(RestEntitySettings), DisplayName = "REST Configuration - Custom API Path without Methods")]
        [DataRow(null, new RestMethod[] { RestMethod.Get, RestMethod.Post }, typeof(RestStoredProcedureEntitySettings), DisplayName = "REST Configuration - Methods without Path configured")]
        [DataRow(true, new RestMethod[] { RestMethod.Get, RestMethod.Post }, typeof(RestStoredProcedureEntityVerboseSettings), DisplayName = "REST Configuration - Boolean Path with Methods configured")]
        [DataRow("book", new RestMethod[] { RestMethod.Get, RestMethod.Post }, typeof(RestStoredProcedureEntityVerboseSettings), DisplayName = "REST Configuration - Custom Path with Methods configured")]
        public void TestGetRestDetails(
                object? restPath,
                RestMethod[]? restMethods,
                Type expectedRestObjectType)
        {
            // When the rest is a boolean object
            object? restDetails = GetRestDetails(restDetail: restPath, restMethods: restMethods);
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, expectedRestObjectType);
        }

        /// <summary>
        /// Test to validate the successful creation of GraphQL configuration for 
        /// various possible GraphQL Type and Operation combinations
        /// </summary>
        [TestMethod]
        [Ignore]
        public void TestGetGraphQLDetails()
        {
            object? graphQlDetails = GetGraphQLDetails("true");
            Assert.IsNotNull(graphQlDetails);
            Assert.IsInstanceOfType(graphQlDetails, typeof(bool));
            Assert.IsTrue((bool)graphQlDetails);

            graphQlDetails = GetGraphQLDetails("True");
            Assert.IsNotNull(graphQlDetails);
            Assert.IsInstanceOfType(graphQlDetails, typeof(bool));
            Assert.IsTrue((bool)graphQlDetails);

            graphQlDetails = GetGraphQLDetails("false");
            Assert.IsNotNull(graphQlDetails);
            Assert.IsInstanceOfType(graphQlDetails, typeof(bool));
            Assert.IsFalse((bool)graphQlDetails);

            graphQlDetails = GetGraphQLDetails("False");
            Assert.IsNotNull(graphQlDetails);
            Assert.IsInstanceOfType(graphQlDetails, typeof(bool));
            Assert.IsFalse((bool)graphQlDetails);

            //when graphql is null
            Assert.IsNull(GetGraphQLDetails(null));

            // When graphql is non-boolean string
            graphQlDetails = GetGraphQLDetails("book");
            Assert.AreEqual(new GraphQLEntitySettings(Type: new SingularPlural(Singular: "book", Plural: "books")), graphQlDetails);

            // When graphql is a pair of string for custom singular, plural string.
            graphQlDetails = GetGraphQLDetails("book:plural_books");
            Assert.AreEqual(new GraphQLEntitySettings(Type: new SingularPlural(Singular: "book", Plural: "plural_books")), graphQlDetails);

            // Invalid graphql string
            graphQlDetails = GetGraphQLDetails("book:plural_books:ads");
            Assert.IsNull(graphQlDetails);
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
                File.Create(expectedRuntimeConfigFile);
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
        [DataRow(new string[] { "*" }, SourceType.StoredProcedure, false, DisplayName = "FAIL: Stored-Procedure with wildcard CRUD operation.")]
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
        }
    }
}

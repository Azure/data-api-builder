namespace Cli.Tests
{
    /// <summary>
    /// Tests for Utils methods.
    /// </summary>
    [TestClass]
    public class UtilsTests
    {
        public TestContext TestContext { get; set; }

        /// <summary>
        /// Setup up temporary test files and environment variables for current test session.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            if (!File.Exists($"{CONFIGFILE_NAME}{CONFIG_EXTENSION}"))
            {
                File.Create($"{CONFIGFILE_NAME}{CONFIG_EXTENSION}");
            }

            if (!File.Exists($"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}"))
            {
                File.Create($"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}");
            }

            if (!File.Exists("my-config.json"))
            {
                File.Create("my-config.json");
            }

            TestContext.Properties.Add(RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME, Environment.GetEnvironmentVariable(RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME));
        }

        /// <summary>
        /// Test to check if it successfully creates the rest object
        /// which can be either a boolean value
        /// or a RestEntitySettings object
        /// </summary>
        [TestMethod]
        public void TestGetRestDetails()
        {
            // when the rest is a boolean object
            object? restDetails = GetRestDetails("true");
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, typeof(bool));
            Assert.IsTrue((bool)restDetails);

            restDetails = GetRestDetails("True");
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, typeof(bool));
            Assert.IsTrue((bool)restDetails);

            restDetails = GetRestDetails("false");
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, typeof(bool));
            Assert.IsFalse((bool)restDetails);

            restDetails = GetRestDetails("False");
            Assert.IsNotNull(restDetails);
            Assert.IsInstanceOfType(restDetails, typeof(bool));
            Assert.IsFalse((bool)restDetails);

            // when rest is non-boolean string
            restDetails = GetRestDetails("book");
            Assert.AreEqual(new RestEntitySettings(Route: "/book"), restDetails);
        }

        /// <summary>
        /// Test to check if it successfully creates the graphql object which can be either a boolean value
        /// or a GraphQLEntitySettings object containing graphql type {singular, plural} based on the input
        /// </summary>
        [TestMethod]
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

            // when graphql is non-boolean string
            graphQlDetails = GetGraphQLDetails("book");
            Assert.AreEqual(new GraphQLEntitySettings(Type: new SingularPlural(Singular: "book", Plural: "books")), graphQlDetails);

            // when graphql is a pair of string for custom singular, plural string.
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
        public void TestConfigSelectionBasedOnCliPrecedence(string? environmentValue, string? userProvidedConfigFile, string? expectedRuntimeConfigFile)
        {
            Environment.SetEnvironmentVariable(RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
            string? actualRuntimeConfigFile;
            Assert.IsTrue(TryGetConfigFileBasedOnCliPrecedence(userProvidedConfigFile, out actualRuntimeConfigFile));
            Assert.AreEqual(expectedRuntimeConfigFile, actualRuntimeConfigFile);
        }

        /// <summary>
        /// Clean up temporary test files and reset environment variables
        /// </summary>
        [TestCleanup]
        public void Cleanup()
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

            Environment.SetEnvironmentVariable(RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME, (string)TestContext.Properties[RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME]);
        }
    }
}

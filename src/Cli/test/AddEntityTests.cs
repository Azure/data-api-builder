namespace Cli.Tests
{
    /// <summary>
    /// Tests for Adding new Entity.
    /// </summary>
    [TestClass]
    public class AddEntityTests
    {
        /// <summary>
        /// Setup the logger for CLI
        /// </summary>
        [TestInitialize]
        public void SetupLoggerForCLI()
        {
            Mock<ILogger<ConfigGenerator>> configGeneratorLogger = new();
            Mock<ILogger<Utils>> utilsLogger = new();
            ConfigGenerator.SetLoggerFactoryForCLi(configGeneratorLogger.Object, utilsLogger.Object);
        }

        /// <summary>
        /// Simple test to add a new entity to json config when there is no existing entity.
        /// By Default an empty collection is generated during initialization
        /// entities: {}
        /// </summary>
        [TestMethod]
        public void AddNewEntityWhenEntitiesEmpty()
        {
            AddOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "read,update" },
                entity: "FirstEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: _testRuntimeConfig);

            string initialConfiguration = INITIAL_CONFIG;
            string expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());
            RunTest(options, initialConfiguration, expectedConfiguration);
        }

        /// <summary>
        /// Add second entity to a config.
        /// </summary>
        [TestMethod]
        public void AddNewEntityWhenEntitiesNotEmpty()
        {
            AddOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "*" },
                entity: "SecondEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: _testRuntimeConfig);

            string initialConfiguration = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());
            string configurationWithOneEntity = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());
            string expectedConfiguration = AddPropertiesToJson(configurationWithOneEntity, GetSecondEntityConfiguration());
            RunTest(options, initialConfiguration, expectedConfiguration);

        }

        /// <summary>
        /// Add duplicate entity should fail.
        /// </summary>
        [TestMethod]
        public void AddDuplicateEntity()
        {
            AddOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "*" },
                entity: "FirstEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                policyRequest: null,
                policyDatabase: null,
                config: _testRuntimeConfig);

            string initialConfiguration = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());
            Assert.IsFalse(ConfigGenerator.TryAddNewEntity(options, ref initialConfiguration));
        }

        /// <summary>
        /// Entity names should be case-sensitive. Adding a new entity with the an existing name but with
        /// a different case in one or more characters should be successful.
        /// </summary>
        [TestMethod]
        public void AddEntityWithAnExistingNameButWithDifferentCase()
        {
            AddOptions options = new(
               source: "MyTable",
               permissions: new string[] { "anonymous", "*" },
                entity: "FIRSTEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: _testRuntimeConfig
            );

            string initialConfiguration = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());
            string configurationWithOneEntity = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());
            string expectedConfiguration = AddPropertiesToJson(configurationWithOneEntity, GetConfigurationWithCaseSensitiveEntityName());
            RunTest(options, initialConfiguration, expectedConfiguration);
        }

        /// <summary>
        /// Add Entity with Policy and Field properties
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "*" }, new string[] { "level", "rating" }, "@claims.name eq 'dab'", "@claims.id eq @item.id", "PolicyAndFields", DisplayName = "Check adding new Entity with both Policy and Fields")]
        [DataRow(new string[] { }, new string[] { }, "@claims.name eq 'dab'", "@claims.id eq @item.id", "Policy", DisplayName = "Check adding new Entity with Policy")]
        [DataRow(new string[] { "*" }, new string[] { "level", "rating" }, null, null, "Fields", DisplayName = "Check adding new Entity with fieldsToInclude and FieldsToExclude")]
        public void AddEntityWithPolicyAndFieldProperties(IEnumerable<string>? fieldsToInclude,
                                                            IEnumerable<string>? fieldsToExclude,
                                                            string? policyRequest,
                                                            string? policyDatabase,
                                                            string check)
        {
            AddOptions options = new(
               source: "MyTable",
               permissions: new string[] { "anonymous", "delete" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: fieldsToInclude,
                fieldsToExclude: fieldsToExclude,
                policyRequest: policyRequest,
                policyDatabase: policyDatabase,
                config: _testRuntimeConfig
            );

            string? expectedConfiguration = null;
            switch (check)
            {
                case "PolicyAndFields":
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, ENTITY_CONFIG_WITH_POLCIY_AND_ACTION_FIELDS);
                    break;
                case "Policy":
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, ENTITY_CONFIG_WITH_POLICY);
                    break;
                case "Fields":
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, ENTITY_CONFIG_WITH_ACTION_FIELDS);
                    break;
            }

            RunTest(options, INITIAL_CONFIG, expectedConfiguration!);
        }

        /// <summary>
        /// Simple test to add a new entity to json config where source is a stored procedure.
        /// </summary>
        [TestMethod]
        public void AddNewEntityWhenEntitiesWithSourceAsStoredProcedure()
        {
            AddOptions options = new(
                source: "s001.book",
                permissions: new string[] { "anonymous", "read" },
                entity: "MyEntity",
                sourceType: "stored-procedure",
                sourceParameters: new string[] { "param1:123", "param2:hello", "param3:true" },
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: _testRuntimeConfig);

            string initialConfiguration = INITIAL_CONFIG;
            string expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);
            RunTest(options, initialConfiguration, expectedConfiguration);
        }

        /// <summary>
        /// Simple test to verify success on adding a new entity with source object for valid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, null, null, "*", true, DisplayName = "Both KeyFields and Parameters not provided for source")]
        [DataRow("stored-procedure", new string[] { "param1:value1" }, null, "create", true, DisplayName = "SourceParameters correctly included with stored procedure")]
        [DataRow("Stored-Procedure", new string[] { "param1:value1" }, null, "read", true, DisplayName = "Stored procedure type check for Case Insensitivity")]
        [DataRow("stored-procedure", new string[] { "param1:value1" }, null, "*", false, DisplayName = "Stored procedure incorrectly configured with wildcard CRUD action")]
        [DataRow("view", null, new string[] { "col1", "col2" }, "*", true, DisplayName = "Source KeyFields correctly included with with View")]
        [DataRow("table", null, new string[] { "col1", "col2" }, "*", true, DisplayName = "Source KeyFields correctly included with with Table")]
        [DataRow(null, null, new string[] { "col1", "col2" }, "*", true, DisplayName = "Source Type of table created when type not specified")]
        [DataRow(null, new string[] { "param1:value1" }, new string[] { "col1", "col2" }, "*", false, DisplayName = "KeyFields and Parameters incorrectly configured for default sourceType")]
        [DataRow("stored-procedure", null, new string[] { "col1", "col2" }, "*", false, DisplayName = "KeyFields incorrectly configured with stored procedure")]
        [DataRow("stored-procedure", new string[] { "param1:value1,param1:223" }, null, "*", false, DisplayName = "Parameters containing duplicate keys are not allowed")]
        [DataRow("view", new string[] { "param1:value1" }, null, "*", false, DisplayName = "Source Parameters incorrectly used with View")]
        [DataRow("table", new string[] { "param1:value1" }, null, "*", false, DisplayName = "Source Parameters incorrectly used with Table")]
        [DataRow("table-view", new string[] { "param1:value1" }, null, "*", false, DisplayName = "Invalid Source Type")]
        public void TestAddNewEntityWithSourceObjectHavingValidFields(
            string? sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields,
            string operations,
            bool expectSuccess)
        {
            AddOptions options = new(
                source: "testSource",
                permissions: new string[] { "anonymous", operations },
                entity: "book",
                sourceType: sourceType,
                sourceParameters: parameters,
                sourceKeyFields: keyFields,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: _testRuntimeConfig);

            string runtimeConfig = INITIAL_CONFIG;

            Assert.AreEqual(expectSuccess, ConfigGenerator.TryAddNewEntity(options, ref runtimeConfig));
        }

        /// <summary>
        /// Check failure when adding an entity with permission containing invalid operations
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "anonymous", "*,create,read" }, DisplayName = "Permission With Wildcard And Other CRUD operations")]
        [DataRow(new string[] { "anonymous", "create,create,read" }, DisplayName = "Permission With duplicate CRUD operations")]
        [DataRow(new string[] { "anonymous", "fetch" }, DisplayName = "Invalid CRUD operation: fetch")]
        [DataRow(new string[] { "anonymous", "fetch,*" }, DisplayName = "WILDCARD combined with other operations")]
        [DataRow(new string[] { "anonymous", "fetch,create" }, DisplayName = "Mix of invalid and valid CRUD operations")]
        [DataRow(new string[] { "anonymous", "reads,create" }, DisplayName = "Misspelled CRUD operations")]
        [DataRow(new string[] { }, DisplayName = "No permissions entered")]
        public void TestAddEntityPermissionWithInvalidOperation(IEnumerable<string> permissions)
        {

            AddOptions options = new(
                source: "MyTable",
                permissions: permissions,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" },
                policyRequest: null,
                policyDatabase: null,
                config: _testRuntimeConfig);

            string runtimeConfig = INITIAL_CONFIG;

            Assert.IsFalse(ConfigGenerator.TryAddNewEntity(options, ref runtimeConfig));
        }

        /// <summary>
        /// Call ConfigGenerator.TryAddNewEntity and verify json result.
        /// </summary>
        /// <param name="options">Add options.</param>
        /// <param name="initialConfig">Initial Json configuration.</param>
        /// <param name="expectedConfig">Expected Json output.</param>
        private static void RunTest(AddOptions options, string initialConfig, string expectedConfig)
        {
            Assert.IsTrue(ConfigGenerator.TryAddNewEntity(options, ref initialConfig));

            JObject expectedJson = JObject.Parse(expectedConfig);
            JObject actualJson = JObject.Parse(initialConfig);

            Assert.IsTrue(JToken.DeepEquals(expectedJson, actualJson));
        }

        private static string GetFirstEntityConfiguration()
        {
            return @"
              {
                ""entities"": {
                    ""FirstEntity"": {
                      ""source"": ""MyTable"",
                      ""permissions"": [
                          {
                          ""role"": ""anonymous"",
                          ""actions"": [""read"",""update""]
                          }
                        ]
                    }
                }
            }";
        }

        private static string GetSecondEntityConfiguration()
        {
            return @"{
              ""entities"": {
                    ""SecondEntity"": {
                      ""source"": ""MyTable"",
                      ""permissions"": [
                        {
                          ""role"": ""anonymous"",
                          ""actions"": [""*""]
                        }
                      ]
                    }
                  }
                }";
        }

        private static string GetConfigurationWithCaseSensitiveEntityName()
        {
            return @"
                {
                ""entities"": {
                    ""FIRSTEntity"": {
                    ""source"": ""MyTable"",
                    ""permissions"": [
                        {
                        ""role"": ""anonymous"",
                        ""actions"": [""*""]
                        }
                    ]
                    }
                }
              }
            ";
        }

    }

}

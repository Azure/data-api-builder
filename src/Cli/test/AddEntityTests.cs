namespace Cli.Tests
{
    /// <summary>
    /// Tests for Adding new Entity.
    /// </summary>
    [TestClass]
    public class AddEntityTests
    {
        #region Positive Tests

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

            string initialConfiguration = GetInitialConfiguration;
            string expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetFirstEntityConfiguration());
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

            string initialConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetFirstEntityConfiguration());
            string configurationWithOneEntity = AddPropertiesToJson(GetInitialConfiguration, GetFirstEntityConfiguration());
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

            string initialConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetFirstEntityConfiguration());
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

            string initialConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetFirstEntityConfiguration());
            string configurationWithOneEntity = AddPropertiesToJson(GetInitialConfiguration, GetFirstEntityConfiguration());
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
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetEntityConfigurationWithPolicyAndFields);
                    break;
                case "Policy":
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetEntityConfigurationWithPolicy);
                    break;
                case "Fields":
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetEntityConfigurationWithFields);
                    break;
            }

            RunTest(options, GetInitialConfiguration, expectedConfiguration!);
        }

        /// <summary>
        /// Simple test to add a new entity to json config where source is a stored procedure.
        /// </summary>
        [TestMethod]
        public void AddNewEntityWhenEntitiesWithSourceAsStoredProcedure()
        {
            AddOptions options = new(
                source: "s001.book",
                permissions: new string[] { "anonymous", "*" },
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

            string initialConfiguration = GetInitialConfiguration;
            string expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntityWithSourceAsStoredProcedure);
            RunTest(options, initialConfiguration, expectedConfiguration);
        }

        /// <summary>
        /// Simple test to verify success on adding a new entity with source object for valid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, null, null, DisplayName = "Both KeyFields and Parameters provided for source.")]
        [DataRow("stored-procedure", new string[] { "param1:value1" }, null, DisplayName = "SourceParameters with stored procedure.")]
        [DataRow("view", null, new string[] { "col1", "col2" }, DisplayName = "Source KeyFields with View")]
        [DataRow("table", null, new string[] { "col1", "col2" }, DisplayName = "Source KeyFields with Table")]
        [DataRow(null, null, new string[] { "col1", "col2" }, DisplayName = "Source KeyFields with SourceType not provided")]
        public void TestAddNewEntityWithSourceObjectHavingValidFields(
            string? sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields
        )
        {
            AddOptions options = new(
                source: "testSource",
                permissions: new string[] { "anonymous", "*" },
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

            string runtimeConfig = GetInitialConfiguration;

            Assert.IsTrue(ConfigGenerator.TryAddNewEntity(options, ref runtimeConfig));
        }

        #endregion

        #region Negative Tests

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

            string runtimeConfig = GetInitialConfiguration;

            Assert.IsFalse(ConfigGenerator.TryAddNewEntity(options, ref runtimeConfig));
        }

        /// <summary>
        /// Simple test to verify failure on adding a new entity with source object for invalid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, new string[] { "param1:value1" }, new string[] { "col1", "col2" }, DisplayName = "Both KeyFields and Parameters provided for source.")]
        [DataRow("stored-procedure", null, new string[] { "col1", "col2" }, DisplayName = "KeyFields with stored procedure.")]
        [DataRow("view", new string[] { "param1:value1" }, null, DisplayName = "Source Parameters with View")]
        [DataRow("table", new string[] { "param1:value1" }, null, DisplayName = "Source Parameters with Table")]
        public void TestAddNewEntityWithSourceObjectForInvalidFields(
            string? sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields
        )
        {
            AddOptions options = new(
                source: "testSource",
                permissions: new string[] { "anonymous", "*" },
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

            string runtimeConfig = GetInitialConfiguration;

            Assert.IsFalse(ConfigGenerator.TryAddNewEntity(options, ref runtimeConfig));
        }

        #endregion

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

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
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: "outputfile");

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
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: "outputfile");

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
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                policyRequest: null,
                policyDatabase: null,
                config: "outputfile");

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
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: "outputfile"
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
        [DataRow(new string[] { "*" }, new string[] { "level", "rating" }, "@claims.name eq 'hawaii'", "@claims.id eq @item.id", "PolicyAndFields", DisplayName = "Check adding new Entity with both Policy and Fields")]
        [DataRow(new string[] { }, new string[] { }, "@claims.name eq 'hawaii'", "@claims.id eq @item.id", "Policy", DisplayName = "Check adding new Entity with Policy")]
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
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: fieldsToInclude,
                fieldsToExclude: fieldsToExclude,
                policyRequest: policyRequest,
                policyDatabase: policyDatabase,
                config: "outputfile"
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
        public void TestAddEntityPermissionWithInvalidAction(IEnumerable<string> permissions)
        {

            AddOptions options = new(
                source: "MyTable",
                permissions: permissions,
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" },
                policyRequest: null,
                policyDatabase: null,
                config: "outputfile");

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

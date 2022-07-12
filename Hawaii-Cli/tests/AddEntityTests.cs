namespace Hawaii.Cli.Tests
{
    /// <summary>
    /// Tests for Adding new Entity.
    /// </summary>
    [TestClass]
    public class AddEntityTests
    {
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
                permissions: "anonymous:read,update",
                entity: "FirstEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                name: "outputfile");

            string initialConfiguration = GetInitialConfiguration();
            string expectedConfiguration = AddEntityPropertiesToConfiguration(GetInitialConfiguration(), GetFirstEntityConfiguration());
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
                permissions: "anonymous:*",
                entity: "SecondEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                name: "outputfile");

            string initialConfiguration = AddEntityPropertiesToConfiguration(GetInitialConfiguration(), GetFirstEntityConfiguration());
            string configurationWithOneEntity = AddEntityPropertiesToConfiguration(GetInitialConfiguration(), GetFirstEntityConfiguration());
            string expectedConfiguration = AddEntityPropertiesToConfiguration(configurationWithOneEntity, GetSecondEntityConfiguration());
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
                permissions: "anonymous:*",
                entity: "FirstEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                name: "outputfile");

            string initialConfiguration = AddEntityPropertiesToConfiguration(GetInitialConfiguration(), GetFirstEntityConfiguration());
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
               permissions: "anonymous:*",
                entity: "FIRSTEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                name: "outputfile"
            );

            string initialConfiguration = AddEntityPropertiesToConfiguration(GetInitialConfiguration(), GetFirstEntityConfiguration());
            string configurationWithOneEntity = AddEntityPropertiesToConfiguration(GetInitialConfiguration(), GetFirstEntityConfiguration());
            string expectedConfiguration = AddEntityPropertiesToConfiguration(configurationWithOneEntity, GetConfigurationWithCaseSensitiveEntityName());
            RunTest(options, initialConfiguration, expectedConfiguration);
        }

        #region Negative Tests

        /// <summary>
        /// Check failure when adding an entity with permission containing invalid actions
        /// </summary>
        [DataTestMethod]
        [DataRow("anonymous:*,create,read", DisplayName = "Permission With Wildcard And Other CRUD Actions")]
        [DataRow("anonymous:create,create,read", DisplayName = "Permission With duplicate CRUD Actions")]
        [DataRow("anonymous:fetch", DisplayName = "Invalid CRUD action: fetch")]
        [DataRow("anonymous:fetch,*", DisplayName = "WILDCARD combined with other actions")]
        [DataRow("anonymous:fetch,create", DisplayName = "Mix of invalid and valid CRUD action")]
        [DataRow("anonymous:reads,create", DisplayName = "Misspelled CRUD actions")]
        public void TestAddEntityPermissionWithInvalidAction(string permissions)
        {
            AddOptions options = new(
                source: "MyTable",
                permissions: permissions,
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: "id,rating",
                fieldsToExclude: "level",
                name: "outputfile");

            string runtimeConfig = GetInitialConfiguration();

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

        /// <summary>
        /// Adds the entity properties to the configuration and returns the updated configuration json as a string.
        /// </summary>
        /// <param name="configuration">Configuration Json.</param>
        /// <param name="entityProperties">Entity properties to be added to the configuration.</param>
        private static string AddEntityPropertiesToConfiguration(string configuration, string entityProperties)
        {
            JObject configurationJson = JObject.Parse(configuration);
            JObject entityPropertiesJson = JObject.Parse(entityProperties);

            configurationJson.Merge(entityPropertiesJson, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Union
            });
            return configurationJson.ToString();
        }

        private static string GetInitialConfiguration()
        {
            return @"{
            ""$schema"": ""hawaii.draft-01.schema.json"",
            ""data-source"": {
              ""database-type"": ""mssql"",
              ""connection-string"": ""testconnectionstring""
            },
            ""mssql"": {
              ""set-session-context"": true
            },
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
              ""host"": {
                ""mode"": ""development"",
                ""cors"": {
                  ""origins"": [],
                  ""allow-credentials"": true
                },
                ""authentication"": {
                  ""provider"": ""EasyAuth""
                }
              }
            },
            ""entities"": {}
          }";

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
                          ""actions"": [
                              ""read"",
                              ""update""
                            ]
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
                          ""actions"": [ ""*"" ]
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
                        ""actions"": [
                            ""*""
                        ]
                        }
                    ]
                    }
                }
              }
            ";
        }

    }

}

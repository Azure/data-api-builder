// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions.TestingHelpers;
using System.IO.Abstractions;
using System.Reflection;
using Cli.Commands;
using Snapshooter.MSTest;

namespace Cli.Tests
{
    /// <summary>
    /// Tests for Adding new Entity.
    /// </summary>
    [TestClass]
    public class AddEntityTests
    {
        [TestInitialize]
        public void TestInitialize()
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });

            SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
            SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? runtimeConfig);

            Assert.IsTrue(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig updatedRuntimeConfig));

            Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

            Snapshot.Match(updatedRuntimeConfig!);
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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            string initialConfiguration = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());

            RuntimeConfigLoader.TryParseConfig(initialConfiguration, out RuntimeConfig? runtimeConfig);

            Assert.IsTrue(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig updatedRuntimeConfig));

            Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

            Snapshot.Match(updatedRuntimeConfig!);
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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string initialConfiguration = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());
            RuntimeConfigLoader.TryParseConfig(initialConfiguration, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig updatedRuntimeConfig));

            Assert.AreSame(runtimeConfig!, updatedRuntimeConfig);
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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            string initialConfiguration = AddPropertiesToJson(INITIAL_CONFIG, GetFirstEntityConfiguration());
            RuntimeConfigLoader.TryParseConfig(initialConfiguration, out RuntimeConfig? runtimeConfig);

            Assert.IsTrue(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig updatedRuntimeConfig));

            Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

            Snapshot.Match(updatedRuntimeConfig!);
        }

        /// <summary>
        /// Add Entity with Policy and Field properties
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "*" }, new string[] { "level", "rating" }, "@claims.name eq 'dab'", "@claims.id eq @item.id", DisplayName = "Check adding new Entity with both Policy and Fields")]
        [DataRow(new string[] { }, new string[] { }, "@claims.name eq 'dab2'", "@claims.id eq @item.id", DisplayName = "Check adding new Entity with Policy")]
        [DataRow(new string[] { "*" }, new string[] { "level", "rating" }, "null", "null", DisplayName = "Check adding new Entity with fieldsToInclude and FieldsToExclude")]
        public void AddEntityWithPolicyAndFieldProperties(
            IEnumerable<string>? fieldsToInclude,
            IEnumerable<string>? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase)
        {
            // these bits are to work around these two bugs:
            // - https://github.com/SwissLife-OSS/snapshooter/issues/178
            // - https://github.com/SwissLife-OSS/snapshooter/issues/180
            if (policyRequest == "null")
            {
                policyRequest = null;
            }

            if (policyDatabase == "null")
            {
                policyDatabase = null;
            }

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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? runtimeConfig);

            Assert.IsTrue(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig updatedRuntimeConfig));

            Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

            Snapshot.Match(updatedRuntimeConfig!);
        }

        /// <summary>
        /// Simple test to add a new entity to json config where source is a stored procedure.
        /// </summary>
        [TestMethod]
        public void AddNewEntityWhenEntitiesWithSourceAsStoredProcedure()
        {
            AddOptions options = new(
                source: "s001.book",
                permissions: new string[] { "anonymous", "execute" },
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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? runtimeConfig);

            Assert.IsTrue(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig updatedRuntimeConfig));

            Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

            Snapshot.Match(updatedRuntimeConfig!);
        }

        /// <summary>
        /// Tests that the CLI Add command translates the user provided options into the expected configuration file.
        /// This test validates that the stored procedure entity configuration JSON contains the execute permission as well as
        /// the explicitly configured REST methods (Post, Put, Patch) and GraphQL operation (Query).
        /// </summary>
        [TestMethod]
        public void TestAddStoredProcedureWithRestMethodsAndGraphQLOperations()
        {
            AddOptions options = new(
                source: "s001.book",
                permissions: new string[] { "anonymous", "execute" },
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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: new string[] { "Post", "Put", "Patch" },
                graphQLOperationForStoredProcedure: "Query"
                );

            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? runtimeConfig);

            Assert.IsTrue(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig updatedRuntimeConfig));

            Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

            Snapshot.Match(updatedRuntimeConfig!);
        }

        /// <summary>
        /// Simple test to verify success on adding a new entity with source object for valid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, null, null, "*", true, DisplayName = "Both KeyFields and Parameters not provided for source")]
        [DataRow("stored-procedure", new string[] { "param1:value1" }, null, "execute", true, DisplayName = "SourceParameters correctly included with stored procedure")]
        [DataRow("Stored-Procedure", new string[] { "param1:value1" }, null, "execute", true, DisplayName = "Stored procedure type check for Case Insensitivity")]
        [DataRow("stored-procedure", new string[] { "param1:value1" }, null, "*", true, DisplayName = "Stored procedure correctly configured with wildcard CRUD action")]
        [DataRow("view", null, new string[] { "col1", "col2" }, "*", true, DisplayName = "Source KeyFields correctly included with View")]
        [DataRow("view", null, null, "*", false, DisplayName = "Mandatory KeyFields not provided with View")]
        [DataRow("table", null, new string[] { "col1", "col2" }, "*", true, DisplayName = "Source KeyFields correctly included with Table")]
        [DataRow(null, null, new string[] { "col1", "col2" }, "*", true, DisplayName = "Source Type of table created when type not specified")]
        [DataRow(null, new string[] { "param1:value1" }, new string[] { "col1", "col2" }, "*", false, DisplayName = "KeyFields and Parameters incorrectly configured for default sourceType")]
        [DataRow("stored-procedure", null, new string[] { "col1", "col2" }, "*", false, DisplayName = "KeyFields incorrectly configured with stored procedure")]
        [DataRow("stored-procedure", new string[] { "param1:value1,param1:223" }, null, "*", false, DisplayName = "Parameters containing duplicate keys are not allowed")]
        [DataRow("view", new string[] { "param1:value1" }, null, "*", false, DisplayName = "Source Parameters incorrectly used with View")]
        [DataRow("table", new string[] { "param1:value1" }, null, "*", false, DisplayName = "Source Parameters incorrectly used with Table")]
        [DataRow("view", new string[] { "param1:value1" }, new string[] { "col1", "col2" }, "*", false, DisplayName = "Source Parameters and Keyfields incorrectly used with View")]
        [DataRow("table", new string[] { "param1:value1" }, new string[] { "col1", "col2" }, "*", false, DisplayName = "Source Parameters and Keyfields incorrectly used with Table")]
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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? runtimeConfig);

            Assert.AreEqual(expectSuccess, TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig _));
        }

        /// <summary>
        /// Validates the successful/unsuccessful execution of ConfigGenerator.TryAddNewEntity()
        /// by passing AddOptions for a stored procedure with various combinations of REST Path, REST Methods,
        /// GraphQL Type, and GraphQL Operation.
        /// Failure is limited to when GraphQL and REST explicit options are provided, but the associated
        /// REST/GraphQL endpoint for the entity is disabled.
        /// </summary>
        /// <param name="restMethods">Explicitly configured REST methods for stored procedure.</param>
        /// <param name="graphQLOperation">Explicitly configured GraphQL operation for stored procedure (Query/Mutation).</param>
        /// <param name="restRoute">Custom REST route</param>
        /// <param name="graphQLType">Whether GraphQL is explicitly enabled/disabled on the entity.</param>
        /// <param name="testType">Scenario that is tested. It is used for constructing the expected JSON.</param>
        [DataTestMethod]
        [DataRow(new string[] { }, "null", "null", "null", "NoOptions", DisplayName = "Default Case without any customization")]
        [DataRow(new string[] { }, "null", "true", "null", "RestEnabled", DisplayName = "REST enabled without any methods explicitly configured")]
        [DataRow(new string[] { }, "null", "book", "null", "CustomRestPath", DisplayName = "Custom REST path defined without any methods explicitly configured")]
        [DataRow(new string[] { "Get", "Post", "Patch" }, "null", "null", "null", "RestMethods", DisplayName = "REST methods defined without REST Path explicitly configured")]
        [DataRow(new string[] { "Get", "Post", "Patch" }, "null", "true", "null", "RestEnabledWithMethods", DisplayName = "REST enabled along with some methods")]
        [DataRow(new string[] { "Get", "Post", "Patch" }, "null", "book", "null", "CustomRestPathWithMethods", DisplayName = "Custom REST path defined along with some methods")]
        [DataRow(new string[] { }, "null", "null", "true", "GQLEnabled", DisplayName = "GraphQL enabled without any operation explicitly configured")]
        [DataRow(new string[] { }, "null", "null", "book", "GQLCustomType", DisplayName = "Custom GraphQL Type defined without any operation explicitly configured")]
        [DataRow(new string[] { }, "null", "null", "book:books", "GQLSingularPluralCustomType", DisplayName = "SingularPlural GraphQL Type enabled without any operation explicitly configured")]
        [DataRow(new string[] { }, "Query", "null", "true", "GQLEnabledWithCustomOperation", DisplayName = "GraphQL enabled with Query operation")]
        [DataRow(new string[] { }, "Query", "null", "book", "GQLCustomTypeAndOperation", DisplayName = "Custom GraphQL Type defined along with Query operation")]
        [DataRow(new string[] { }, "Query", "null", "book:books", "GQLSingularPluralTypeAndOperation", DisplayName = "SingularPlural GraphQL Type defined along with Query operation")]
        [DataRow(new string[] { }, "null", "true", "true", "RestAndGQLEnabled", DisplayName = "Both REST and GraphQL enabled without any methods and operations configured explicitly")]
        [DataRow(new string[] { "Get" }, "Query", "true", "true", "CustomRestMethodAndGqlOperation", DisplayName = "Both REST and GraphQL enabled with custom REST methods and GraphQL operations")]
        [DataRow(new string[] { "Post", "Patch", "Put" }, "Query", "book", "book:books", "CustomRestAndGraphQLAll", DisplayName = "Configuration with REST Path, Methods and GraphQL Type, Operation")]
        public void TestAddNewSpWithDifferentRestAndGraphQLOptions(
                IEnumerable<string>? restMethods,
                string? graphQLOperation,
                string? restRoute,
                string? graphQLType,
                string testType
            )
        {
            // these bits are to work around these two bugs:
            // - https://github.com/SwissLife-OSS/snapshooter/issues/178
            // - https://github.com/SwissLife-OSS/snapshooter/issues/180
            if (!restMethods!.Any())
            {
                restMethods = null;
            }

            if (graphQLOperation == "null")
            {
                graphQLOperation = null;
            }

            if (restRoute == "null")
            {
                restRoute = null;
            }

            if (graphQLType == "null")
            {
                graphQLType = null;
            }

            AddOptions options = new(
                source: "s001.book",
                permissions: new string[] { "anonymous", "execute" },
                entity: "MyEntity",
                sourceType: "stored-procedure",
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: restRoute,
                graphQLType: graphQLType,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: restMethods,
                graphQLOperationForStoredProcedure: graphQLOperation
                );

            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? runtimeConfig);

            Assert.IsTrue(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig updatedRuntimeConfig));

            Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

            Snapshot.Match(updatedRuntimeConfig!);
        }

        [DataTestMethod]
        [DataRow(new string[] { }, "Mutation", "true", "false", DisplayName = "Conflicting configurations - GraphQL operation specified but entity is disabled for GraphQL")]
        [DataRow(new string[] { "Get" }, "null", "false", "true", DisplayName = "Conflicting configurations - REST methods specified but entity is disabled for REST")]
        public void TestAddStoredProcedureWithConflictingRestGraphQLOptions(
            IEnumerable<string>? restMethods,
                string? graphQLOperation,
                string? restRoute,
                string? graphQLType
                )
        {
            // these bits are to work around these two bugs:
            // - https://github.com/SwissLife-OSS/snapshooter/issues/178
            // - https://github.com/SwissLife-OSS/snapshooter/issues/180
            if (!restMethods!.Any())
            {
                restMethods = null;
            }

            if (graphQLOperation == "null")
            {
                graphQLOperation = null;
            }

            if (restRoute == "null")
            {
                restRoute = null;
            }

            if (graphQLType == "null")
            {
                graphQLType = null;
            }

            AddOptions options = new(
                source: "s001.book",
                permissions: new string[] { "anonymous", "execute" },
                entity: "MyEntity",
                sourceType: "stored-procedure",
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: restRoute,
                graphQLType: graphQLType,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: restMethods,
                graphQLOperationForStoredProcedure: graphQLOperation
            );

            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig _));
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
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryAddNewEntity(options, runtimeConfig!, out RuntimeConfig _));
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
    }

}

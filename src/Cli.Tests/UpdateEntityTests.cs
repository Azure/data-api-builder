// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.Converters;

namespace Cli.Tests
{
    /// <summary>
    /// Tests for Updating Entity.
    /// </summary>
    [TestClass]
    public class UpdateEntityTests : VerifyBase
    {
        [TestInitialize]
        public void TestInitialize()
        {
            ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();
            SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
            SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
        }

        /// <summary>
        /// Simple test to update an entity permission by adding a new action.
        /// Initially it contained only "read" and "update". adding a new action "create"
        /// </summary>
        [TestMethod, Description("it should update the permission by adding a new action.")]
        public Task TestUpdateEntityPermission()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "MyTable",
                permissions: new string[] { "anonymous", "create" },
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" });

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
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

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Simple test to update an entity permission by creating a new role.
        /// Initially the role "authenticated" was not present, so it will create a new role.
        /// </summary>
        [TestMethod, Description("it should update the permission by adding a new role.")]
        public Task TestUpdateEntityPermissionByAddingNewRole()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "MyTable",
                permissions: new string[] { "authenticated", "*" },
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" }
            );

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"" : [""read"", ""update""]
                                    }
                                ]
                            }
                        }
                    }";

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Simple test to update the action which already exists in permissions.
        /// Adding fields to Include/Exclude to update action.
        /// </summary>
        [TestMethod, Description("Should update the action which already exists in permissions.")]
        public Task TestUpdateEntityPermissionWithExistingAction()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "MyTable",
                permissions: new string[] { "anonymous", "update" },
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" }
                );

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [ ""read"", ""update""]
                                    }
                                ]
                            }
                        }
                    }";

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Simple test to update an entity permission which has action as WILDCARD.
        /// It will update only "read" and "delete".
        /// </summary>
        [TestMethod, Description("it should update the permission which has action as WILDCARD.")]
        public Task TestUpdateEntityPermissionHavingWildcardAction()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "MyTable",
                permissions: new string[] { "anonymous", "read,delete" },
                fieldsToInclude: new string[] { "id", "type", "quantity" },
                fieldsToExclude: new string[] { }
                );

            string initialConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            {
                                                ""action"": ""*"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""rating""],
                                                    ""exclude"": [""level""]
                                                }
                                            }
                                        ]
                                    }
                                ]
                            }
                        }
                    }";
            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Simple test to update an entity permission with new action as WILDCARD.
        /// It will apply the update as WILDCARD.
        /// </summary>
        [TestMethod, Description("it should update the permission with \"*\".")]
        public Task TestUpdateEntityPermissionWithWildcardAction()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "MyTable",
                permissions: new string[] { "anonymous", "*" },
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" }
                );

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [""read"", ""update""]
                                    }
                                ]
                            }
                        }
                    }";

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Simple test to update an entity by adding a new relationship.
        /// </summary>
        [TestMethod, Description("it should add a new relationship")]
        public Task TestUpdateEntityByAddingNewRelationship()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                entity: "SecondEntity",
                relationship: "r2",
                cardinality: "many",
                targetEntity: "FirstEntity"
                );

            string initialConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""FirstEntity"": {
                                ""source"": ""Table1"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            ""create"",
                                            ""read""
                                        ]
                                    }
                                ],
                                ""relationships"": {
                                    ""r1"": {
                                        ""cardinality"": ""one"",
                                        ""target.entity"": ""SecondEntity""
                                    }
                                }
                            },
                            ""SecondEntity"": {
                                ""source"": ""Table2"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            ""create"",
                                            ""read""
                                        ]
                                    }
                                ]
                            }
                        }
                    }";
            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Simple test to update an existing relationship.
        /// It will add source.fields, target.fields, linking.object, linking.source.fields, linking.target.fields
        /// </summary>
        [TestMethod, Description("it should update an existing relationship")]
        public Task TestUpdateEntityByModifyingRelationship()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                entity: "SecondEntity",
                relationship: "r2",
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1", "e2,t2" }
                );

            string initialConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""FirstEntity"": {
                                ""source"": ""Table1"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            ""create"",
                                            ""read""
                                        ]
                                    }
                                ],
                                ""relationships"": {
                                    ""r1"": {
                                        ""cardinality"": ""one"",
                                        ""target.entity"": ""SecondEntity""
                                    }
                                }
                            },
                            ""SecondEntity"": {
                                ""source"": ""Table2"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            ""create"",
                                            ""read""
                                        ]
                                    }
                                ],
                                ""relationships"": {
                                    ""r2"": {
                                        ""cardinality"": ""many"",
                                        ""target.entity"": ""FirstEntity""
                                    }
                                }
                            }
                        }
                    }";

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Simple test to update an entity cache.
        /// </summary>
        [TestMethod, Description("It should update the cache into true.")]
        public Task TestUpdateEntityCaching()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "MyTable",
                cacheEnabled: "true",
                cacheTtl: "1"
            );

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""cache"": {
                                    ""enabled"": false,
                                    ""ttlseconds"": 0
                                }
                            }
                        }
                    }";

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Test to check creation of a new relationship
        /// </summary>
        [TestMethod]
        public void TestCreateNewRelationship()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1", "e2,t2" }
                );

            EntityRelationship? relationship = CreateNewRelationshipWithUpdateOptions(options);

            Assert.IsNotNull(relationship);
            Assert.AreEqual(Cardinality.Many, relationship.Cardinality);
            Assert.AreEqual("entity_link", relationship.LinkingObject);
            Assert.AreEqual("FirstEntity", relationship.TargetEntity);
            CollectionAssert.AreEqual(new string[] { "e1" }, relationship.SourceFields);
            CollectionAssert.AreEqual(new string[] { "e2", "t2" }, relationship.TargetFields);
            CollectionAssert.AreEqual(new string[] { "eid1" }, relationship.LinkingSourceFields);
            CollectionAssert.AreEqual(new string[] { "eid2", "fid2" }, relationship.LinkingTargetFields);

        }

        /// <summary>
        /// Test to check creation of a relationship with multiple linking fields
        /// </summary>
        [TestMethod]
        public void TestCreateNewRelationshipWithMultipleLinkingFields()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1", "fid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1", "e2,t2" }
                );

            EntityRelationship? relationship = CreateNewRelationshipWithUpdateOptions(options);

            Assert.IsNotNull(relationship);
            Assert.AreEqual(Cardinality.Many, relationship.Cardinality);
            Assert.AreEqual("entity_link", relationship.LinkingObject);
            Assert.AreEqual("FirstEntity", relationship.TargetEntity);
            CollectionAssert.AreEqual(new string[] { "e1" }, relationship.SourceFields);
            CollectionAssert.AreEqual(new string[] { "e2", "t2" }, relationship.TargetFields);
            CollectionAssert.AreEqual(new string[] { "eid1", "fid1" }, relationship.LinkingSourceFields);
            CollectionAssert.AreEqual(new string[] { "eid2", "fid2" }, relationship.LinkingTargetFields);

        }

        /// <summary>
        /// Test to check creation of a relationship with multiple relationship fields
        /// </summary>
        [TestMethod]
        public void TestCreateNewRelationshipWithMultipleRelationshipFields()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1,t1", "e2,t2" }
                );

            EntityRelationship? relationship = CreateNewRelationshipWithUpdateOptions(options);

            Assert.IsNotNull(relationship);
            Assert.AreEqual(Cardinality.Many, relationship.Cardinality);
            Assert.AreEqual("entity_link", relationship.LinkingObject);
            Assert.AreEqual("FirstEntity", relationship.TargetEntity);
            CollectionAssert.AreEqual(new string[] { "e1", "t1" }, relationship.SourceFields);
            CollectionAssert.AreEqual(new string[] { "e2", "t2" }, relationship.TargetFields);
            CollectionAssert.AreEqual(new string[] { "eid1" }, relationship.LinkingSourceFields);
            CollectionAssert.AreEqual(new string[] { "eid2", "fid2" }, relationship.LinkingTargetFields);

        }

        /// <summary>
        /// Update Entity with new Policy and Field properties
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "*" }, new string[] { "level", "rating" }, "@claims.name eq 'dab'", "@claims.id eq @item.id", "PolicyAndFields", DisplayName = "Check adding new Policy and Fields to Action")]
        [DataRow(new string[] { }, new string[] { }, "@claims.name eq 'dab'", "@claims.id eq @item.id", "Policy", DisplayName = "Check adding new Policy to Action")]
        [DataRow(new string[] { "*" }, new string[] { "level", "rating" }, null, null, "Fields", DisplayName = "Check adding new fieldsToInclude and FieldsToExclude to Action")]
        public Task TestUpdateEntityWithPolicyAndFieldProperties(IEnumerable<string>? fieldsToInclude,
                                                            IEnumerable<string>? fieldsToExclude,
                                                            string? policyRequest,
                                                            string? policyDatabase,
                                                            string check)
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
               source: "MyTable",
               permissions: new string[] { "anonymous", "delete" },
               fieldsToInclude: fieldsToInclude,
               fieldsToExclude: fieldsToExclude,
               policyRequest: policyRequest,
               policyDatabase: policyDatabase);

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY);

            VerifySettings settings = new();
            settings.UseHashedParameters(fieldsToInclude, fieldsToExclude, policyRequest, policyDatabase);
            return ExecuteVerifyTest(initialConfig, options, settings);
        }

        /// <summary>
        /// Simple test to verify success on updating a source from string to source object for valid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow("s001.book", null, new string[] { "anonymous", "*" }, null, null, "UpdateSourceName", DisplayName = "Updating sourceName with no change in parameters or keyfields.")]
        [DataRow(null, "view", null, null, new string[] { "col1", "col2" }, "ConvertToView", DisplayName = "Source KeyFields with View")]
        [DataRow(null, "table", null, null, new string[] { "id", "name" }, "ConvertToTable", DisplayName = "Source KeyFields with Table")]
        [DataRow(null, null, null, null, new string[] { "id", "name" }, "ConvertToDefaultType", DisplayName = "Source KeyFields with SourceType not provided")]
        public Task TestUpdateSourceStringToDatabaseSourceObject(
            string? source,
            string? sourceType,
            string[]? permissions,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields,
            string task)
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                permissions: permissions,
                source: source,
                sourceType: sourceType,
                sourceParameters: parameters,
                sourceKeyFields: keyFields);

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, BASIC_ENTITY_WITH_ANONYMOUS_ROLE);

            VerifySettings settings = new();
            settings.UseHashedParameters(source, sourceType, permissions, parameters, keyFields);
            return ExecuteVerifyTest(initialConfig, options, settings);
        }

        [TestMethod]
        public Task UpdateDatabaseSourceName()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "newSourceName",
                permissions: new string[] { "anonymous", "execute" },
                entity: "MyEntity");

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);

            return ExecuteVerifyTest(initialConfig, options);
        }

        [TestMethod]
        public Task UpdateDatabaseSourceParameters()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                permissions: new string[] { "anonymous", "execute" },
                sourceParameters: new string[] { "param1:dab", "param2:false" }
            );

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);

            return ExecuteVerifyTest(initialConfig, options);
        }

        [TestMethod]
        public Task UpdateDatabaseSourceKeyFields()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                permissions: new string[] { "anonymous", "read" },
                sourceKeyFields: new string[] { "col1", "col2" });

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_SOURCE_AS_TABLE);

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Converts one source object type to another.
        /// Also testing automatic update for parameter and keyfields to null in case
        /// of table/view, and stored-procedure respectively.
        /// Updating Table with all supported CRUD action to Stored-Procedure should fail.
        /// </summary>
        [DataTestMethod]
        [DataRow(SINGLE_ENTITY_WITH_ONLY_READ_PERMISSION, "stored-procedure", new string[] { "param1:123", "param2:hello", "param3:true" },
            null, SINGLE_ENTITY_WITH_STORED_PROCEDURE, new string[] { "anonymous", "execute" }, false, true,
            DisplayName = "PASS - Convert table to stored-procedure with valid parameters.")]
        [DataRow(SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, "stored-procedure", null, new string[] { "col1", "col2" },
            SINGLE_ENTITY_WITH_STORED_PROCEDURE, new string[] { "anonymous", "execute" }, false, false,
            DisplayName = "FAIL - Convert table to stored-procedure with invalid KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, "stored-procedure", null, null, SINGLE_ENTITY_WITH_STORED_PROCEDURE, null,
            true, true, DisplayName = "PASS - Convert table with wildcard CRUD operation to stored-procedure.")]
        [DataRow(SINGLE_ENTITY_WITH_STORED_PROCEDURE, "table", null, new string[] { "id", "name" },
            SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, new string[] { "anonymous", "*" }, false, true,
            DisplayName = "PASS - Convert stored-procedure to table with valid KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_STORED_PROCEDURE, "view", null, new string[] { "col1", "col2" },
            SINGLE_ENTITY_WITH_SOURCE_AS_VIEW, new string[] { "anonymous", "*" }, false, true,
            DisplayName = "PASS - Convert stored-procedure to view with valid KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_STORED_PROCEDURE, "table", new string[] { "param1:kind", "param2:true" },
            null, SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, null, false, false,
            DisplayName = "FAIL - Convert stored-procedure to table with parameters is not allowed.")]
        [DataRow(SINGLE_ENTITY_WITH_STORED_PROCEDURE, "table", null, null, SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, null,
            true, true, DisplayName = "PASS - Convert stored-procedure to table with no parameters or KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, "view", null, new string[] { "col1", "col2" },
            SINGLE_ENTITY_WITH_SOURCE_AS_VIEW, null, false, true,
            DisplayName = "PASS - Convert table to view with KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, "view", new string[] { "param1:kind", "param2:true" }, null,
            SINGLE_ENTITY_WITH_SOURCE_AS_VIEW, null, false, false,
            DisplayName = "FAIL - Convert table to view with parameters is not allowed.")]
        public Task TestConversionOfSourceObject(
            string initialSourceObjectEntity,
            string sourceType,
            IEnumerable<string>? parameters,
            string[]? keyFields,
            string updatedSourceObjectEntity,
            string[]? permissions,
            bool expectNoKeyFieldsAndParameters,
            bool expectSuccess)
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "s001.book",
                permissions: permissions,
                sourceType: sourceType,
                sourceParameters: parameters,
                sourceKeyFields: keyFields);

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, initialSourceObjectEntity);
            RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig);
            Assert.AreEqual(expectSuccess, TryUpdateExistingEntity(options, runtimeConfig!, out RuntimeConfig updatedConfig));

            if (expectSuccess)
            {
                Assert.AreNotSame(runtimeConfig, updatedConfig);
                VerifySettings settings = new();
                settings.UseHashedParameters(sourceType, parameters, keyFields, permissions, expectNoKeyFieldsAndParameters);
                return Verify(updatedConfig, settings);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Update Policy for an action
        /// </summary>
        [TestMethod]
        public Task TestUpdatePolicy()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
               source: "MyTable",
               permissions: new string[] { "anonymous", "delete" },
               policyRequest: "@claims.name eq 'api_builder'",
               policyDatabase: "@claims.name eq @item.name"
            );

            string? initialConfig = AddPropertiesToJson(INITIAL_CONFIG, ENTITY_CONFIG_WITH_POLCIY_AND_ACTION_FIELDS);
            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Test to verify updating permissions for stored-procedure.
        /// Checks:
        /// 1. Updating a stored-procedure with WILDCARD/CRUD action should fail.
        /// 2. Adding a new role/Updating an existing role with execute action should succeeed.
        /// </summary>
        [DataTestMethod]
        [DataRow("anonymous", "*", true, DisplayName = "PASS: Stored-Procedure updated with wildcard operation")]
        [DataRow("anonymous", "execute", true, DisplayName = "PASS: Stored-Procedure updated with execute operation")]
        [DataRow("anonymous", "create,read", false, DisplayName = "FAIL: Stored-Procedure updated with CRUD action.")]
        [DataRow("authenticated", "*", true, DisplayName = "PASS: Stored-Procedure updated with wildcard operation for an existing role.")]
        [DataRow("authenticated", "execute", true, DisplayName = "PASS: Stored-Procedure updated with execute operation for an existing role.")]
        [DataRow("authenticated", "create,read", false, DisplayName = "FAIL: Stored-Procedure updated with CRUD action for an existing role.")]
        public void TestUpdatePermissionsForStoredProcedure(
            string role,
            string operations,
            bool isSuccess
        )
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "my_sp",
                permissions: new string[] { role, operations },
                sourceType: "stored-procedure");

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);
            RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig);

            Assert.AreEqual(isSuccess, TryUpdateExistingEntity(options, runtimeConfig!, out RuntimeConfig _));
        }

        /// <summary>
        /// Test to Update Entity with New mappings
        /// </summary>
        [TestMethod]
        public Task TestUpdateEntityWithMappings()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(map: new string[] { "id:Identity", "name:Company Name" });

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
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

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Test to Update stored procedure action. Stored procedures support only execute action.
        /// An attempt to update to another action should be unsuccessful.
        /// </summary>
        [TestMethod]
        public void TestUpdateActionOfStoredProcedureRole()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                permissions: new string[] { "authenticated", "create" }
                );

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": {
                                    ""object"": ""MySp"",
                                    ""type"": ""stored-procedure""
                                },
                                ""permissions"": [
                                    {
                                    ""role"": ""anonymous"",
                                    ""actions"": [
                                            ""execute""
                                        ]
                                    },
                                    {
                                    ""role"": ""authenticated"",
                                    ""actions"": [
                                            ""execute""
                                        ]
                                    }
                                ]
                            }
                        }
                    }";

            RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryUpdateExistingEntity(options, runtimeConfig!, out RuntimeConfig _));
        }

        /// <summary>
        /// Test to Update Entity with New mappings containing special unicode characters
        /// </summary>
        [TestMethod]
        public Task TestUpdateEntityWithSpecialCharacterInMappings()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                map: new string[] { "Macaroni:Mac & Cheese", "region:United State's Region", "russian:русский", "chinese:中文" }
            );

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
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

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Test to Update existing mappings of an entity
        /// </summary>
        [TestMethod]
        public Task TestUpdateExistingMappings()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                map: new string[] { "name:Company Name", "addr:Company Address", "number:Contact Details" }
            );

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [""read"",""update""]
                                    }
                                ],
                                ""mappings"": {
                                    ""id"": ""Identity"",
                                    ""name"": ""Company Name""
                                }
                            }
                        }
                    }";

            return ExecuteVerifyTest(initialConfig, options);
        }

        /// <summary>
        /// Test to validate various updates to various combinations of
        /// REST path, REST methods, GraphQL Type and GraphQL Operation are working as intended.
        /// </summary>
        /// <param name="restMethods">List of REST Methods that are configured for the entity</param>
        /// <param name="graphQLOperation">GraphQL Operation configured for the entity</param>
        /// <param name="restRoute">REST Path configured for the entity</param>
        /// <param name="graphQLType">GraphQL Type configured for the entity</param>
        /// <param name="testType">Scenario that is tested. It is also used to construct the expected JSON.</param>
        [DataTestMethod]
        [DataRow(null, null, "true", null, "RestEnabled", DisplayName = "Entity Update - REST enabled without any methods explicitly configured")]
        [DataRow(null, null, "book", null, "CustomRestPath", DisplayName = "Entity Update - Custom REST path defined without any methods explicitly configured")]
        [DataRow(new string[] { "Get", "Post", "Patch" }, null, null, null, "RestMethods", DisplayName = "Entity Update - REST methods defined without REST Path explicitly configured")]
        [DataRow(new string[] { "Get", "Post", "Patch" }, null, "true", null, "RestEnabledWithMethods", DisplayName = "Entity Update - REST enabled along with some methods")]
        [DataRow(new string[] { "Get", "Post", "Patch" }, null, "book", null, "CustomRestPathWithMethods", DisplayName = "Entity Update - Custom REST path defined along with some methods")]
        [DataRow(null, null, null, "true", "GQLEnabled", DisplayName = "Entity Update - GraphQL enabled without any operation explicitly configured")]
        [DataRow(null, null, null, "book", "GQLCustomType", DisplayName = "Entity Update - Custom GraphQL Type defined without any operation explicitly configured")]
        [DataRow(null, null, null, "book:books", "GQLSingularPluralCustomType", DisplayName = "Entity Update - SingularPlural GraphQL Type enabled without any operation explicitly configured")]
        [DataRow(null, "Query", null, "true", "GQLEnabledWithCustomOperation", DisplayName = "Entity Update - GraphQL enabled with Query operation")]
        [DataRow(null, "Query", null, "book", "GQLCustomTypeAndOperation", DisplayName = "Entity Update - Custom GraphQL Type defined along with Query operation")]
        [DataRow(null, "Query", null, "book:books", "GQLSingularPluralTypeAndOperation", DisplayName = "Entity Update - SingularPlural GraphQL Type defined along with Query operation")]
        [DataRow(null, null, "true", "true", "RestAndGQLEnabled", DisplayName = "Entity Update - Both REST and GraphQL enabled without any methods and operations configured explicitly")]
        [DataRow(null, null, "false", "false", "RestAndGQLDisabled", DisplayName = "Entity Update - Both REST and GraphQL disabled without any methods and operations configured explicitly")]
        [DataRow(new string[] { "Get" }, "Query", "true", "true", "CustomRestMethodAndGqlOperation", DisplayName = "Entity Update - Both REST and GraphQL enabled with custom REST methods and GraphQL operations")]
        [DataRow(new string[] { "Post", "Patch", "Put" }, "Query", "book", "book:books", "CustomRestAndGraphQLAll", DisplayName = "Entity Update - Configuration with REST Path, Methods and GraphQL Type, Operation")]
        public Task TestUpdateRestAndGraphQLSettingsForStoredProcedures(
            IEnumerable<string>? restMethods,
            string? graphQLOperation,
            string? restRoute,
            string? graphQLType,
            string testType)
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                restRoute: restRoute,
                graphQLType: graphQLType,
                restMethodsForStoredProcedure: restMethods,
                graphQLOperationForStoredProcedure: graphQLOperation
            );

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SP_DEFAULT_REST_METHODS_GRAPHQL_OPERATION);

            VerifySettings settings = new();
            settings.UseHashedParameters(restMethods, graphQLOperation, restRoute, graphQLType, testType);
            return ExecuteVerifyTest(initialConfig, options, settings);
        }

        /// <summary>
        /// Validates that updating an entity with conflicting options such as disabling an entity
        /// for GraphQL but specifying GraphQL Operations results in a failure. Likewise for REST Path and
        /// Methods.
        /// </summary>
        /// <param name="restMethods"></param>
        /// <param name="graphQLOperation"></param>
        /// <param name="restRoute"></param>
        /// <param name="graphQLType"></param>
        [DataTestMethod]
        [DataRow(null, "Mutation", "true", "false", DisplayName = "Conflicting configurations during update - GraphQL operation specified but entity is disabled for GraphQL")]
        [DataRow(new string[] { "Get" }, null, "false", "true", DisplayName = "Conflicting configurations during update - REST methods specified but entity is disabled for REST")]
        public void TestUpdateStoredProcedureWithConflictingRestGraphQLOptions(
            IEnumerable<string>? restMethods,
                string? graphQLOperation,
                string restRoute,
                string graphQLType
                )
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                restRoute: restRoute,
                graphQLType: graphQLType,
                restMethodsForStoredProcedure: restMethods,
                graphQLOperationForStoredProcedure: graphQLOperation);

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SP_DEFAULT_REST_METHODS_GRAPHQL_OPERATION);
            RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryUpdateExistingEntity(options, runtimeConfig!, out RuntimeConfig _));
        }

        /// <summary>
        /// Simple test to update an entity permission with new action containing WILDCARD and other crud operation.
        /// Example "*,read,create"
        /// Update including WILDCARD along with other crud operation is not allowed
        /// </summary>
        [TestMethod, Description("update action should fail because of invalid action combination.")]
        public void TestUpdateEntityPermissionWithWildcardAndOtherCRUDAction()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "MyTable",
                permissions: new string[] { "anonymous", "*,create,read" },
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" });

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
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

            RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryUpdateExistingEntity(options, runtimeConfig!, out RuntimeConfig _));
        }

        /// <summary>
        /// Simple test to verify failure on updating source of an entity with invalid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, new string[] { "param1:value1" }, new string[] { "col1", "col2" }, "anonymous", "*", DisplayName = "Both KeyFields and Parameters provided for source")]
        [DataRow("stored-procedure", null, new string[] { "col1", "col2" }, "anonymous", "create", DisplayName = "KeyFields incorrectly used with stored procedure")]
        [DataRow("stored-procedure", new string[] { "param1:value1,param1:223" }, null, "anonymous", "read", DisplayName = "Parameters with duplicate keys for stored procedure")]
        [DataRow("stored-procedure", new string[] { "param1:value1,param2:223" }, null, "anonymous", "create,read", DisplayName = "Stored procedure with more than 1 CRUD operation")]
        [DataRow("stored-procedure", new string[] { "param1:value1,param2:223" }, null, "anonymous", "*", DisplayName = "Stored procedure with wildcard CRUD operation")]
        [DataRow("view", new string[] { "param1:value1" }, null, "anonymous", "*", DisplayName = "Source Parameters incorrectly used with View")]
        [DataRow("view", null, null, "anonymous", "*", DisplayName = "Mandatory KeyFields for views not provided.")]
        [DataRow("view", new string[] { "param1:value1" }, new string[] { "col1", "col2" }, "anonymous", "*", DisplayName = "Source Parameters and keyfields incorrectly used with View.")]
        [DataRow("table", new string[] { "param1:value1" }, null, "anonymous", "*", DisplayName = "Source Parameters incorrectly used with Table")]
        [DataRow("table", new string[] { "param1:value1" }, new string[] { "col1", "col2" }, "anonymous", "*", DisplayName = "Source Parameters and keyfields incorrectly used with Table.")]
        [DataRow("table-view", new string[] { "param1:value1" }, null, "anonymous", "*", DisplayName = "Invalid Source Type")]
        public void TestUpdateSourceObjectWithInvalidFields(
            string? sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields,
            string role,
            string operations)
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                source: "MyTable",
                permissions: new string[] { role, operations },
                sourceType: sourceType,
                sourceParameters: parameters,
                sourceKeyFields: keyFields);

            string initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);

            RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryUpdateExistingEntity(options, runtimeConfig!, out RuntimeConfig _));
        }

        /// <summary>
        /// Test to check failure on invalid permission string
        /// </summary>
        [TestMethod]
        public void TestParsingFromInvalidPermissionString()
        {
            string? role, actions;
            IEnumerable<string> permissions = new string[] { "anonymous,create" }; //wrong format
            bool isSuccess = TryGetRoleAndOperationFromPermission(permissions, out role, out actions);

            Assert.IsFalse(isSuccess);
            Assert.IsNull(role);
            Assert.IsNull(actions);
        }

        /// <summary>
        /// Test to check creation of a new relationship with Invalid Mapping fields
        /// </summary>
        [TestMethod]
        public void TestCreateNewRelationshipWithInvalidRelationshipFields()
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1,e2,t2" } // Invalid value. Correct format uses ':' to separate source and target fields
            );

            EntityRelationship? relationship = CreateNewRelationshipWithUpdateOptions(options);

            Assert.IsNull(relationship);
        }

        /// <summary>
        /// Test to Update Entity with Invalid mappings
        /// </summary>
        [DataTestMethod]
        [DataRow("id:identity:id,name:Company Name", DisplayName = "Invalid format for mappings value, required: 2, provided: 3.")]
        [DataRow("id:identity:id,name:", DisplayName = "Invalid format for mappings value, required: 2, provided: 1.")]
        public void TestUpdateEntityWithInvalidMappings(string mappings)
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                map: mappings.Split(',')
            );

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
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

            RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryUpdateExistingEntity(options, runtimeConfig!, out RuntimeConfig _));
        }

        /// <summary>
        /// Test to validate that Permissions is mandatory when using options --fields.include or --fields.exclude
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { }, new string[] { "field" }, new string[] { }, DisplayName = "Invalid command with fieldsToInclude but no permissions")]
        [DataRow(new string[] { }, new string[] { }, new string[] { "field1,field2" }, DisplayName = "Invalid command with fieldsToExclude but no permissions")]
        public void TestUpdateEntityWithInvalidPermissionAndFields(
            IEnumerable<string> Permissions,
            IEnumerable<string> FieldsToInclude,
            IEnumerable<string> FieldsToExclude)
        {
            UpdateOptions options = GenerateBaseUpdateOptions(
                permissions: Permissions,
                fieldsToInclude: FieldsToInclude,
                fieldsToExclude: FieldsToExclude);

            string initialConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [""read"",""update""]
                                    }
                                ],
                                ""mappings"": {
                                    ""id"": ""Identity"",
                                    ""name"": ""Company Name""
                                }
                            }
                        }
                    }";
            RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig);

            Assert.IsFalse(TryUpdateExistingEntity(options, runtimeConfig!, out RuntimeConfig _));
        }

        /// <summary>
        /// Test to verify Invalid inputs to create a relationship
        /// </summary>
        [DataTestMethod]
        [DataRow("CosmosDB_NoSQL", "one", "MyEntity", DisplayName = "CosmosDb does not support relationships")]
        [DataRow("mssql", null, "MyEntity", DisplayName = "Cardinality should not be null")]
        [DataRow("mssql", "manyx", "MyEntity", DisplayName = "Cardinality should be one/many")]
        [DataRow("mssql", "one", null, DisplayName = "Target entity should not be null")]
        [DataRow("mssql", "one", "InvalidEntity", DisplayName = "Target Entity should be present in config to create a relationship")]
        public void TestVerifyCanUpdateRelationshipInvalidOptions(string db, string cardinality, string targetEntity)
        {
            RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(EnumExtensions.Deserialize<DatabaseType>(db), "", new()),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>())
            );

            Assert.IsFalse(VerifyCanUpdateRelationship(runtimeConfig, cardinality: cardinality, targetEntity: targetEntity));
        }

        /// <summary>
        /// Test to verify that adding a relationship to an entity which has GraphQL disabled should fail.
        /// The test created 2 entities. One entity has GQL enabled which tries to create relationship with
        /// another entity which has GQL disabled which is invalid.
        /// </summary>
        [TestMethod]
        public void EnsureFailure_AddRelationshipToEntityWithDisabledGraphQL()
        {
            EntityAction actionForRole = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: new(null, null));

            EntityPermission permissionForEntity = new(
                Role: "anonymous",
                Actions: new[] { actionForRole });

            Entity sampleEntity1 = new(
                Source: new("SOURCE1", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new("SOURCE1", "SOURCE1s"),
                Permissions: new[] { permissionForEntity },
                Relationships: null,
                Mappings: null
            );

            // entity with graphQL disabled
            Entity sampleEntity2 = new(
                Source: new("SOURCE2", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new("SOURCE2", "SOURCE2s", false),
                Permissions: new[] { permissionForEntity },
                Relationships: null,
                Mappings: null
            );

            Dictionary<string, Entity> entityMap = new()
            {
                { "SampleEntity1", sampleEntity1 },
                { "SampleEntity2", sampleEntity2 }
            };

            RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "", new()),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null)),
                Entities: new(entityMap)
            );

            Assert.IsFalse(VerifyCanUpdateRelationship(runtimeConfig, cardinality: "one", targetEntity: "SampleEntity2"));
        }

        /// <summary>
        /// Test to verify updating the description property of an entity.
        /// </summary>
        [TestMethod]
        public void TestUpdateEntityDescription()
        {
            // Initial config with an old description
            string initialConfig = GetInitialConfigString() + "," + @"
                ""entities"": {
                    ""MyEntity"": {
                        ""source"": ""MyTable"",
                        ""description"": ""Old description"",
                        ""permissions"": [
                            {
                                ""role"": ""anonymous"",
                                ""actions"": [""read""]
                            }
                        ]
                    }
                }
            }";

            // UpdateOptions with a new description
            UpdateOptions options = GenerateBaseUpdateOptions(
                entity: "MyEntity",
                description: "Updated description"
            );

            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig), "Parsed config file.");
            Assert.IsTrue(TryUpdateExistingEntity(options, runtimeConfig, out RuntimeConfig updatedRuntimeConfig), "Successfully updated entity in the config.");

            // Assert that the description was updated
            Assert.AreEqual("Updated description", updatedRuntimeConfig.Entities["MyEntity"].Description);
        }

        private static string GetInitialConfigString()
        {
            return @"{" +
                        @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
                        @"""data-source"": {
                            ""database-type"": ""mssql"",
                            ""connection-string"": """ + SAMPLE_TEST_CONN_STRING + @"""
                        },
                        ""runtime"": {
                            ""rest"": {
                                ""enabled"": true,
                                ""path"": ""/""
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
                                    ""allow-credentials"": false
                                },
                                ""authentication"": {
                                    ""provider"": ""StaticWebApps"",
                                    ""jwt"": {
                                    ""audience"": """",
                                    ""issuer"": """"
                                    }
                                }
                            }
                        }";
        }

        private static UpdateOptions GenerateBaseUpdateOptions(
            string? source = null,
            IEnumerable<string>? permissions = null,
            string entity = "MyEntity",
            string? sourceType = null,
            IEnumerable<string>? sourceParameters = null,
            IEnumerable<string>? sourceKeyFields = null,
            string? restRoute = null,
            string? graphQLType = null,
            IEnumerable<string>? fieldsToInclude = null,
            IEnumerable<string>? fieldsToExclude = null,
            string? policyRequest = null,
            string? policyDatabase = null,
            string? relationship = null,
            string? cardinality = null,
            string? targetEntity = null,
            string? linkingObject = null,
            IEnumerable<string>? linkingSourceFields = null,
            IEnumerable<string>? linkingTargetFields = null,
            IEnumerable<string>? relationshipFields = null,
            IEnumerable<string>? map = null,
            IEnumerable<string>? restMethodsForStoredProcedure = null,
            string? graphQLOperationForStoredProcedure = null,
            string? cacheEnabled = null,
            string? cacheTtl = null,
            string? description = null
            )
        {
            return new(
                source: source,
                permissions: permissions,
                entity: entity,
                sourceType: sourceType,
                sourceParameters: sourceParameters,
                sourceKeyFields: sourceKeyFields,
                restRoute: restRoute,
                graphQLType: graphQLType,
                fieldsToInclude: fieldsToInclude,
                fieldsToExclude: fieldsToExclude,
                policyRequest: policyRequest,
                policyDatabase: policyDatabase,
                relationship: relationship,
                cardinality: cardinality,
                targetEntity: targetEntity,
                linkingObject: linkingObject,
                linkingSourceFields: linkingSourceFields,
                linkingTargetFields: linkingTargetFields,
                relationshipFields: relationshipFields,
                map: map,
                cacheEnabled: cacheEnabled,
                cacheTtl: cacheTtl,
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: restMethodsForStoredProcedure,
                graphQLOperationForStoredProcedure: graphQLOperationForStoredProcedure,
                description: description
            );
        }

        private Task ExecuteVerifyTest(string initialConfig, UpdateOptions options, VerifySettings? settings = null)
        {
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(initialConfig, out RuntimeConfig? runtimeConfig), "Parsed config file.");

            Assert.IsTrue(TryUpdateExistingEntity(options, runtimeConfig, out RuntimeConfig updatedRuntimeConfig), "Successfully updated entity in the config.");

            Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

            return Verify(updatedRuntimeConfig, settings);
        }
    }
}

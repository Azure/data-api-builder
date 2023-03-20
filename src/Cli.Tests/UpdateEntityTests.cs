// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Tests for Updating Entity.
    /// </summary>
    [TestClass]
    public class UpdateEntityTests
    {
        /// <summary>
        /// Setup the logger for CLI
        /// </summary>
        [TestInitialize]
        public void SetupLoggerForCLI()
        {
            TestHelper.SetupTestLoggerForCLI();
        }

        #region  Positive Tests
        /// <summary>
        /// Simple test to update an entity permission by adding a new action.
        /// Initially it contained only "read" and "update". adding a new action "create"
        /// </summary>
        [TestMethod, Description("it should update the permission by adding a new action.")]
        public void TestUpdateEntityPermission()
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "create" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" },
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null)
                ;

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            {
                                                ""action"": ""Create"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""rating""],
                                                    ""exclude"": [""level""]
                                                }
                                            },
                                            ""Read"",
                                            ""Update""
                                        ],
                                    }
                                ]
                            }
                        }
                    }";

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Simple test to update an entity permission by creating a new role.
        /// Initially the role "authenticated" was not present, so it will create a new role.
        /// </summary>
        [TestMethod, Description("it should update the permission by adding a new role.")]
        public void TestUpdateEntityPermissionByAddingNewRole()
        {

            UpdateOptions options = new(
                source: "MyTable",
                permissions: new string[] { "authenticated", "*" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" },
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [""read"",""update""]
                                    },
                                    {
                                        ""role"": ""authenticated"",
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

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Simple test to update the action which already exists in permissions.
        /// Adding fields to Include/Exclude to update action.
        /// </summary>
        [TestMethod, Description("Should update the action which already exists in permissions.")]
        public void TestUpdateEntityPermissionWithExistingAction()
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "update" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" },
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            {
                                                ""action"": ""Update"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""rating""],
                                                    ""exclude"": [""level""]
                                                }
                                            },
                                            ""Read""
                                        ]
                                    }
                                ]
                            }
                        }
                    }";

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Simple test to update an entity permission which has action as WILDCARD.
        /// It will update only "read" and "delete".
        /// </summary>
        [TestMethod, Description("it should update the permission which has action as WILDCARD.")]
        public void TestUpdateEntityPermissionHavingWildcardAction()
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "read,delete" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { "id", "type", "quantity" },
                fieldsToExclude: new string[] { },
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            {
                                                ""action"": ""Read"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""type"", ""quantity""],
                                                    ""exclude"": []
                                                }
                                            },
                                            {
                                                ""action"": ""Delete"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""type"", ""quantity""],
                                                    ""exclude"": []
                                                }
                                            },
                                            {
                                                ""action"": ""Create"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""rating""],
                                                    ""exclude"": [""level""]
                                                }
                                            },
                                            {
                                                ""action"": ""Update"",
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

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Simple test to update an entity permission with new action as WILDCARD.
        /// It will apply the update as WILDCARD.
        /// </summary>
        [TestMethod, Description("it should update the permission with \"*\".")]
        public void TestUpdateEntityPermissionWithWildcardAction()
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "*" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" },
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: null,
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
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

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Simple test to update an entity by adding a new relationship.
        /// </summary>
        [TestMethod, Description("it should add a new relationship")]
        public void TestUpdateEntityByAddingNewRelationship()
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "SecondEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                relationship: "r2",
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
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

            bool isSuccess = ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig);

            Assert.IsTrue(isSuccess);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Simple test to update an existing relationship.
        /// It will add source.fields, target.fields, linking.object, linking.source.fields, linking.target.fields
        /// </summary>
        [TestMethod, Description("it should update an existing relationship")]
        public void TestUpdateEntityByModifyingRelationship()
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "SecondEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                relationship: "r2",
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1", "e2,t2" },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
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
                                        ""target.entity"": ""FirstEntity"",
                                        ""source.fields"": [""e1""],
                                        ""target.fields"": [""e2"", ""t2""],
                                        ""linking.object"": ""entity_link"",
                                        ""linking.source.fields"": [""eid1""],
                                        ""linking.target.fields"": [""eid2"", ""fid2""]
                                    }
                                }
                            }
                        }
                    }";

            bool isSuccess = TryUpdateExistingEntity(options, ref runtimeConfig);
            Assert.IsTrue(isSuccess);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Test to check creation of a new relationship
        /// </summary>
        [TestMethod]
        public void TestCreateNewRelationship()
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                relationship: null,
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1", "e2,t2" },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            Relationship? relationship = CreateNewRelationshipWithUpdateOptions(options);

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
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                relationship: null,
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1", "fid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1", "e2,t2" },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            Relationship? relationship = CreateNewRelationshipWithUpdateOptions(options);

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
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                relationship: null,
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1,t1", "e2,t2" },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            Relationship? relationship = CreateNewRelationshipWithUpdateOptions(options);

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
        public void TestUpdateEntityWithPolicyAndFieldProperties(IEnumerable<string>? fieldsToInclude,
                                                            IEnumerable<string>? fieldsToExclude,
                                                            string? policyRequest,
                                                            string? policyDatabase,
                                                            string check)
        {

            UpdateOptions options = new(
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
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            string? actualConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY);
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

            Assert.IsTrue(TryUpdateExistingEntity(options, ref actualConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfiguration!), JObject.Parse(actualConfig)));
        }

        /// <summary>
        /// Simple test to verify success on updating a source from string to source object for valid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow("s001.book", null, new string[] { "anonymous", "*" }, null, null, "UpdateSourceName", DisplayName = "Updating sourceName with no change in parameters or keyfields.")]
        [DataRow(null, "view", null, null, new string[] { "col1", "col2" }, "ConvertToView", DisplayName = "Source KeyFields with View")]
        [DataRow(null, "table", null, null, new string[] { "id", "name" }, "ConvertToTable", DisplayName = "Source KeyFields with Table")]
        [DataRow(null, null, null, null, new string[] { "id", "name" }, "ConvertToDefaultType", DisplayName = "Source KeyFields with SourceType not provided")]
        public void TestUpdateSourceStringToDatabaseSourceObject(
            string? source,
            string? sourceType,
            string[]? permissions,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields,
            string task)
        {

            UpdateOptions options = new(
                source: source,
                permissions: permissions,
                entity: "MyEntity",
                sourceType: sourceType,
                sourceParameters: parameters,
                sourceKeyFields: keyFields,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            string? actualConfig = AddPropertiesToJson(INITIAL_CONFIG, BASIC_ENTITY_WITH_ANONYMOUS_ROLE);
            string? expectedConfiguration;
            switch (task)
            {
                case "UpdateSourceName":
                    actualConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY);
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, BASIC_ENTITY_WITH_ANONYMOUS_ROLE);
                    break;
                case "ConvertToView":
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_SOURCE_AS_VIEW);
                    break;
                default:
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_SOURCE_AS_TABLE);
                    break;
            }

            Assert.IsTrue(TryUpdateExistingEntity(options, ref actualConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfiguration!), JObject.Parse(actualConfig)));
        }

        /// <summary>
        /// Validate behavior of updating a source's value type from string to object.
        /// </summary>
        /// <param name="source">Name of database object.</param>
        /// <param name="parameters">Stored Procedure Parameters</param>
        /// <param name="keyFields">Primary key fields</param>
        /// <param name="permissionConfig">Permissions role:action</param>
        /// <param name="task">Denotes which test/assertion is made on updated entity.</param>
        [DataTestMethod]
        [DataRow("newSourceName", null, null, new string[] { "anonymous", "execute" }, "UpdateSourceName", DisplayName = "Update Source Name of the source object.")]
        [DataRow(null, new string[] { "param1:dab", "param2:false" }, null, new string[] { "anonymous", "execute" }, "UpdateParameters", DisplayName = "Update Parameters of stored procedure.")]
        [DataRow(null, null, new string[] { "col1", "col2" }, new string[] { "anonymous", "read" }, "UpdateKeyFields", DisplayName = "Update KeyFields for table/view.")]
        public void TestUpdateDatabaseSourceObject(
            string? source,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields,
            IEnumerable<string>? permissionConfig,
            string task)
        {
            UpdateOptions options = new(
                source: source,
                permissions: permissionConfig,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: parameters,
                sourceKeyFields: keyFields,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            string? initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);
            switch (task)
            {
                case "UpdateSourceName":
                    AssertUpdatedValuesForSourceObject(
                        options,
                        initialConfig,
                        entityName: "MyEntity",
                        oldSourceName: "s001.book",
                        updatedSourceName: "newSourceName",
                        oldSourceType: SourceType.StoredProcedure,
                        updatedSourceType: SourceType.StoredProcedure,
                        oldParameters: new Dictionary<string, object>() { { "param1", 123 }, { "param2", "hello" }, { "param3", true } },
                        updatedParameters: new Dictionary<string, object>() { { "param1", 123 }, { "param2", "hello" }, { "param3", true } },
                        oldKeyFields: null,
                        updatedKeyFields: null
                    );
                    break;

                case "UpdateParameters":
                    AssertUpdatedValuesForSourceObject(
                        options,
                        initialConfig,
                        entityName: "MyEntity",
                        oldSourceName: "s001.book",
                        updatedSourceName: "s001.book",
                        oldSourceType: SourceType.StoredProcedure,
                        updatedSourceType: SourceType.StoredProcedure,
                        oldParameters: new Dictionary<string, object>() { { "param1", 123 }, { "param2", "hello" }, { "param3", true } },
                        updatedParameters: new Dictionary<string, object>() { { "param1", "dab" }, { "param2", false } },
                        oldKeyFields: null,
                        updatedKeyFields: null
                    );
                    break;

                case "UpdateKeyFields":
                    initialConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_SOURCE_AS_TABLE);
                    AssertUpdatedValuesForSourceObject(
                        options,
                        initialConfig,
                        entityName: "MyEntity",
                        oldSourceName: "s001.book",
                        updatedSourceName: "s001.book",
                        oldSourceType: SourceType.Table,
                        updatedSourceType: SourceType.Table,
                        oldParameters: null,
                        updatedParameters: null,
                        oldKeyFields: new string[] { "id", "name" },
                        updatedKeyFields: new string[] { "col1", "col2" }
                    );
                    break;
            }
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
            DisplayName = "PASS:Convert table to stored-procedure with valid parameters.")]
        [DataRow(SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, "stored-procedure", null, new string[] { "col1", "col2" },
            SINGLE_ENTITY_WITH_STORED_PROCEDURE, new string[] { "anonymous", "execute" }, false, false,
            DisplayName = "FAIL:Convert table to stored-procedure with invalid KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, "stored-procedure", null, null, SINGLE_ENTITY_WITH_STORED_PROCEDURE, null,
            true, true, DisplayName = "PASS:Convert table with wildcard CRUD operation to stored-procedure.")]
        [DataRow(SINGLE_ENTITY_WITH_STORED_PROCEDURE, "table", null, new string[] { "id", "name" },
            SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, new string[] { "anonymous", "*" }, false, true,
            DisplayName = "PASS:Convert stored-procedure to table with valid KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_STORED_PROCEDURE, "view", null, new string[] { "col1", "col2" },
            SINGLE_ENTITY_WITH_SOURCE_AS_VIEW, new string[] { "anonymous", "*" }, false, true,
            DisplayName = "PASS:Convert stored-procedure to view with valid KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_STORED_PROCEDURE, "table", new string[] { "param1:kind", "param2:true" },
            null, SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, null, false, false,
            DisplayName = "FAIL:Convert stored-procedure to table with parameters is not allowed.")]
        [DataRow(SINGLE_ENTITY_WITH_STORED_PROCEDURE, "table", null, null, SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, null,
            true, true, DisplayName = "PASS:Convert stored-procedure to table with no parameters or KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, "view", null, new string[] { "col1", "col2" },
            SINGLE_ENTITY_WITH_SOURCE_AS_VIEW, null, false, true,
            DisplayName = "PASS:Convert table to view with KeyFields.")]
        [DataRow(SINGLE_ENTITY_WITH_SOURCE_AS_TABLE, "view", new string[] { "param1:kind", "param2:true" }, null,
            SINGLE_ENTITY_WITH_SOURCE_AS_VIEW, null, false, false,
            DisplayName = "FAIL:Convert table to view with parameters is not allowed.")]
        public void TestConversionOfSourceObject(
            string initialSourceObjectEntity,
            string sourceType,
            IEnumerable<string>? parameters,
            string[]? keyFields,
            string updatedSourceObjectEntity,
            string[]? permissions,
            bool expectNoKeyFieldsAndParameters,
            bool expectSuccess)
        {
            UpdateOptions options = new(
                source: "s001.book",
                permissions: permissions,
                entity: "MyEntity",
                sourceType: sourceType,
                sourceParameters: parameters,
                sourceKeyFields: keyFields,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            string runtimeConfig = AddPropertiesToJson(INITIAL_CONFIG, initialSourceObjectEntity);
            Assert.AreEqual(expectSuccess, ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));

            if (expectSuccess)
            {
                string updatedConfig = AddPropertiesToJson(INITIAL_CONFIG, updatedSourceObjectEntity);
                if (!expectNoKeyFieldsAndParameters)
                {
                    Assert.IsTrue(JToken.DeepEquals(JObject.Parse(runtimeConfig), JObject.Parse(updatedConfig)));
                }
                else
                {
                    Entity entity = GetEntityObjectFromRuntimeConfigJson(runtimeConfig, entityName: "MyEntity");
                    entity.TryPopulateSourceFields();
                    Assert.IsNull(entity.Parameters);
                    Assert.IsNull(entity.KeyFields);
                }
            }

        }

        /// <summary>
        /// Deserialize the given json config and return the entity object for the provided entityName if present.
        /// </summary>
        private static Entity GetEntityObjectFromRuntimeConfigJson(string runtimeConfigJson, string entityName)
        {
            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(runtimeConfigJson, GetSerializationOptions());
            Assert.IsTrue(runtimeConfig!.Entities.ContainsKey(entityName));
            return runtimeConfig!.Entities[entityName];
        }

        /// <summary>
        /// Contains Assert to check only the intended values of source object is updated.
        /// </summary>
        private static void AssertUpdatedValuesForSourceObject(
            UpdateOptions options,
            string initialConfig,
            string entityName,
            string oldSourceName, string updatedSourceName,
            SourceType oldSourceType, SourceType updatedSourceType,
            Dictionary<string, object>? oldParameters, Dictionary<string, object>? updatedParameters,
            string[]? oldKeyFields, string[]? updatedKeyFields)
        {
            Entity entity = GetEntityObjectFromRuntimeConfigJson(initialConfig, entityName);
            entity.TryPopulateSourceFields();
            Assert.AreEqual(oldSourceName, entity.SourceName);
            Assert.AreEqual(oldSourceType, entity.ObjectType);
            Assert.IsTrue(JToken.DeepEquals(
                JToken.FromObject(JsonSerializer.SerializeToElement(oldParameters)),
                JToken.FromObject(JsonSerializer.SerializeToElement(entity.Parameters)))
            );
            CollectionAssert.AreEquivalent(oldKeyFields, entity.KeyFields);
            Assert.IsTrue(TryUpdateExistingEntity(options, ref initialConfig));
            entity = GetEntityObjectFromRuntimeConfigJson(initialConfig, entityName);
            entity.TryPopulateSourceFields();
            Assert.AreEqual(updatedSourceName, entity.SourceName);
            Assert.AreEqual(updatedSourceType, entity.ObjectType);
            Assert.IsTrue(JToken.DeepEquals(
                JToken.FromObject(JsonSerializer.SerializeToElement(updatedParameters)),
                JToken.FromObject(JsonSerializer.SerializeToElement(entity.Parameters)))
            );
            CollectionAssert.AreEquivalent(updatedKeyFields, entity.KeyFields);
        }

        /// <summary>
        /// Update Policy for an action
        /// </summary>
        [TestMethod]
        public void TestUpdatePolicy()
        {
            UpdateOptions options = new(
               source: "MyTable",
               permissions: new string[] { "anonymous", "delete" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: "@claims.name eq 'api_builder'",
                policyDatabase: "@claims.name eq @item.name",
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            string? actualConfig = AddPropertiesToJson(INITIAL_CONFIG, ENTITY_CONFIG_WITH_POLCIY_AND_ACTION_FIELDS);
            string updatedEntityConfigurationWithPolicyAndFields = @"
              {
                ""entities"": {
                    ""MyEntity"": {
                      ""source"": ""MyTable"",
                      ""permissions"": [
                          {
                          ""role"": ""anonymous"",
                          ""actions"": [
                                {
                                    ""action"": ""Delete"",
                                    ""policy"": {
                                        ""request"": ""@claims.name eq 'api_builder'"",
                                        ""database"": ""@claims.name eq @item.name""
                                    },
                                    ""fields"": {
                                        ""include"": [ ""*"" ],
                                        ""exclude"": [ ""level"", ""rating"" ]
                                    }
                                }
                            ]
                          }
                        ]
                    }
                }
            }";
            string? expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, updatedEntityConfigurationWithPolicyAndFields);
            Assert.IsTrue(TryUpdateExistingEntity(options, ref actualConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfiguration!), JObject.Parse(actualConfig)));
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
            UpdateOptions options = new(
                source: "my_sp",
                permissions: new string[] { role, operations },
                entity: "MyEntity",
                sourceType: "stored-procedure",
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);

            Assert.AreEqual(isSuccess, ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
        }

        /// <summary>
        /// Test to Update Entity with New mappings
        /// </summary>
        [TestMethod]
        public void TestUpdateEntityWithMappings()
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: new string[] { "id:Identity", "name:Company Name" },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [""read"", ""update""]
                                    }
                                ],
                                ""mappings"": {
                                    ""id"": ""Identity"",
                                    ""name"": ""Company Name""
                                }
                            }
                        }
                    }";

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Test to Update stored procedure action. Stored procedures support only execute action. 
        /// An attempt to update to another action should be unsuccessful.
        /// </summary>
        [TestMethod]
        public void TestUpdateActionOfStoredProcedureRole()
        {
            UpdateOptions options = new(
                source: null,
                permissions: new string[] { "authenticated", "create" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: null,
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            Assert.IsFalse(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
        }

        /// <summary>
        /// Test to Update Entity with New mappings containing special unicode characters
        /// </summary>
        [TestMethod]
        public void TestUpdateEntityWithSpecialCharacterInMappings()
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: new string[] { "Macaroni:Mac & Cheese", "region:United State's Region", "russian:русский", "chinese:中文" },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            string expectedConfig = GetInitialConfigString() + "," + @"
                    ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [""read"", ""update""]
                                    }
                                ],
                                ""mappings"": {
                                    ""Macaroni"": ""Mac & Cheese"",
                                    ""region"": ""United State's Region"",
                                    ""russian"": ""русский"",
                                    ""chinese"": ""中文""
                                }
                            }
                        }
                    }";

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
        }

        /// <summary>
        /// Test to Update existing mappings of an entity
        /// </summary>
        [TestMethod]
        public void TestUpdateExistingMappings()
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: new string[] { "name:Company Name", "addr:Company Address", "number:Contact Details" },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetConfigWithMappings();

            string expectedConfig = GetInitialConfigString() + "," + @"
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
                                    ""name"": ""Company Name"",
                                    ""addr"": ""Company Address"",
                                    ""number"": ""Contact Details""
                                }
                            }
                        }
                    }";

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(runtimeConfig)));
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
        [DataRow(null, null, "book", null, "CustomRestPath", DisplayName = "Entity Update - Custom REST path defined without any methods explictly configured")]
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
        public void TestUpdateRestAndGraphQLSettingsForStoredProcedures(
            IEnumerable<string>? restMethods,
            string? graphQLOperation,
            string? restRoute,
            string? graphQLType,
            string testType)
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: restRoute,
                graphQLType: graphQLType,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: null,
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: restMethods,
                graphQLOperationForStoredProcedure: graphQLOperation
                );

            string runtimeConfig = AddPropertiesToJson(INITIAL_CONFIG, SP_DEFAULT_REST_METHODS_GRAPHQL_OPERATION);

            string expectedConfiguration = "";
            switch (testType)
            {
                case "RestEnabled":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_DEFAULT_REST_ENABLED);
                    break;
                }
                case "CustomRestPath":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_CUSTOM_REST_PATH);
                    break;
                }
                case "RestMethods":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_CUSTOM_REST_METHODS);
                    break;
                }
                case "RestEnabledWithMethods":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_REST_ENABLED_WITH_CUSTOM_REST_METHODS);
                    break;
                }
                case "CustomRestPathWithMethods":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_CUSTOM_REST_PATH_WITH_CUSTOM_REST_METHODS);
                    break;
                }
                case "GQLEnabled":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_GRAPHQL_ENABLED);
                    break;
                }
                case "GQLCustomType":
                case "GQLSingularPluralCustomType":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_GRAPHQL_CUSTOM_TYPE);
                    break;
                }
                case "GQLEnabledWithCustomOperation":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_GRAPHQL_ENABLED_WITH_CUSTOM_OPERATION);
                    break;
                }
                case "GQLCustomTypeAndOperation":
                case "GQLSingularPluralTypeAndOperation":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_GRAPHQL_ENABLED_WITH_CUSTOM_TYPE_OPERATION);
                    break;
                }
                case "RestAndGQLEnabled":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_REST_GRAPHQL_ENABLED);
                    break;
                }
                case "RestAndGQLDisabled":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_REST_GRAPHQL_DISABLED);
                    break;
                }
                case "CustomRestMethodAndGqlOperation":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_CUSTOM_REST_METHOD_GRAPHQL_OPERATION);
                    break;
                }
                case "CustomRestAndGraphQLAll":
                {
                    expectedConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_CUSTOM_REST_GRAPHQL_ALL);
                    break;
                }
            }

            Assert.IsTrue(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfiguration), JObject.Parse(runtimeConfig)));
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
        public void TestUpdatetoredProcedureWithConflictingRestGraphQLOptions(
            IEnumerable<string>? restMethods,
                string? graphQLOperation,
                string? restRoute,
                string? graphQLType
                )
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: restRoute,
                graphQLType: graphQLType,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: null,
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: restMethods,
                graphQLOperationForStoredProcedure: graphQLOperation
                );

            string initialConfiguration = AddPropertiesToJson(INITIAL_CONFIG, SP_DEFAULT_REST_METHODS_GRAPHQL_OPERATION);
            Assert.IsFalse(ConfigGenerator.TryUpdateExistingEntity(options, ref initialConfiguration));
        }

        #endregion

        #region  Negative Tests

        /// <summary>
        /// Simple test to update an entity permission with new action containing WILDCARD and other crud operation.
        /// Example "*,read,create"
        /// Update including WILDCARD along with other crud operation is not allowed
        /// </summary>
        [TestMethod, Description("update action should fail because of invalid action combination.")]
        public void TestUpdateEntityPermissionWithWildcardAndOtherCRUDAction()
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "*,create,read" },
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { "id", "rating" },
                fieldsToExclude: new string[] { "level" },
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            Assert.IsFalse(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
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
        [DataRow("table", new string[] { "param1:value1" }, null, "anonymous", "*", DisplayName = "Source Parameters incorrectly used with Table")]
        [DataRow("table-view", new string[] { "param1:value1" }, null, "anonymous", "*", DisplayName = "Invalid Source Type")]
        public void TestUpdateSourceObjectWithInvalidFields(
            string? sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields,
            string role,
            string operations)
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: new string[] { role, operations },
                entity: "MyEntity",
                sourceType: sourceType,
                sourceParameters: parameters,
                sourceKeyFields: keyFields,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                map: new string[] { },
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);

            Assert.IsFalse(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
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

            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                relationship: null,
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: new string[] { "eid1" },
                linkingTargetFields: new string[] { "eid2", "fid2" },
                relationshipFields: new string[] { "e1,e2,t2" }, // Invalid value. Correct format uses ':' to separate source and target fields
                policyRequest: null,
                policyDatabase: null,
                map: null,
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            Relationship? relationship = CreateNewRelationshipWithUpdateOptions(options);

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
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: new string[] { },
                fieldsToExclude: new string[] { },
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: mappings.Split(','),
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
                );

            string runtimeConfig = GetInitialConfigString() + "," + @"
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

            Assert.IsFalse(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
        }

        /// <summary>
        /// Test to validate that Permissions is mandatory when using options --fields.include or --fields.exclude
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { }, new string[] { "field" }, new string[] { }, DisplayName = "Invalid command with fieldsToInclude but no permissions")]
        [DataRow(new string[] { }, new string[] { }, new string[] { "field1,field2" }, DisplayName = "Invalid command with fieldsToExclude but no permissions")]
        public void TestUpdateEntityWithInvalidPermissionAndFields(IEnumerable<string> Permissions,
        IEnumerable<string> FieldsToInclude, IEnumerable<string> FieldsToExclude)
        {
            UpdateOptions options = new(
                source: null,
                permissions: Permissions,
                entity: "MyEntity",
                sourceType: null,
                sourceParameters: null,
                sourceKeyFields: null,
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: FieldsToInclude,
                fieldsToExclude: FieldsToExclude,
                policyRequest: null,
                policyDatabase: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: new string[] { },
                linkingTargetFields: new string[] { },
                relationshipFields: new string[] { },
                map: null,
                config: TEST_RUNTIME_CONFIG_FILE,
                restMethodsForStoredProcedure: null,
                graphQLOperationForStoredProcedure: null
            );

            string runtimeConfig = GetConfigWithMappings();
            Assert.IsFalse(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
        }

        /// <summary>
        /// Test to verify Invalid inputs to create a relationship
        /// </summary>
        [DataTestMethod]
        [DataRow("cosmosdb_nosql", "one", "MyEntity", DisplayName = "CosmosDb does not support relationships")]
        [DataRow("mssql", null, "MyEntity", DisplayName = "Cardinality should not be null")]
        [DataRow("mssql", "manyx", "MyEntity", DisplayName = "Cardinality should be one/many")]
        [DataRow("mssql", "one", null, DisplayName = "Target entity should not be null")]
        [DataRow("mssql", "one", "InvalidEntity", DisplayName = "Target Entity should be present in config to create a relationship")]
        public void TestVerifyCanUpdateRelationshipInvalidOptions(string db, string cardinality, string targetEntity)
        {
            RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(Enum.Parse<DatabaseType>(db)),
                RuntimeSettings: new Dictionary<GlobalSettingsType, object>(),
                Entities: new Dictionary<string, Entity>()
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
            PermissionOperation actionForRole = new(
                Name: Operation.Create,
                Fields: null,
                Policy: null);

            PermissionSetting permissionForEntity = new(
                role: "anonymous",
                operations: new object[] { JsonSerializer.SerializeToElement(actionForRole) });

            Entity sampleEntity1 = new(
                Source: JsonSerializer.SerializeToElement("SOURCE1"),
                Rest: true,
                GraphQL: true,
                Permissions: new PermissionSetting[] { permissionForEntity },
                Relationships: null,
                Mappings: null
            );

            // entity with graphQL disabled
            Entity sampleEntity2 = new(
                Source: JsonSerializer.SerializeToElement("SOURCE2"),
                Rest: true,
                GraphQL: false,
                Permissions: new PermissionSetting[] { permissionForEntity },
                Relationships: null,
                Mappings: null
            );

            Dictionary<string, Entity> entityMap = new();
            entityMap.Add("SampleEntity1", sampleEntity1);
            entityMap.Add("SampleEntity2", sampleEntity2);

            RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(DatabaseType.mssql),
                RuntimeSettings: new Dictionary<GlobalSettingsType, object>(),
                Entities: entityMap
            );

            Assert.IsFalse(VerifyCanUpdateRelationship(runtimeConfig, cardinality: "one", targetEntity: "SampleEntity2"));
        }

        #endregion

        private static string GetInitialConfigString()
        {
            return @"{" +
                        @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
                        @"""data-source"": {
                            ""database-type"": ""mssql"",
                            ""connection-string"": ""testconnectionstring""
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

        private static string GetConfigWithMappings()
        {
            return GetInitialConfigString() + "," + @"
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
        }
    }
}

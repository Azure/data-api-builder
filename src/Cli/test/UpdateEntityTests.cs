namespace Cli.Tests
{
    /// <summary>
    /// Tests for Updating Entity.
    /// </summary>
    [TestClass]
    public class UpdateEntityTests
    {
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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig
            );

            string? actualConfig = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntity);
            string? expectedConfiguration = null;
            switch (check)
            {
                case "PolicyAndFields":
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetEntityConfigurationWithPolicyAndFieldsGeneratedWithUpdateCommand);
                    break;
                case "Policy":
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetEntityConfigurationWithPolicyWithUpdateCommand);
                    break;
                case "Fields":
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetEntityConfigurationWithFieldsGeneratedWithUpdateCommand);
                    break;
            }

            Assert.IsTrue(TryUpdateExistingEntity(options, ref actualConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfiguration!), JObject.Parse(actualConfig)));
        }

        /// <summary>
        /// Simple test to verify success on updating a source from string to source object for valid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow("s001.book", null, null, null, "UpdateSourceName", DisplayName = "Both KeyFields and Parameters provided for source.")]
        [DataRow(null, "stored-procedure", new string[] { "param1:123", "param2:hello", "param3:true" }, null, "ConvertToStoredProcedure", DisplayName = "SourceParameters with stored procedure.")]
        [DataRow(null, "view", null, new string[] { "id", "name" }, "ConvertToView", DisplayName = "Source KeyFields with View")]
        [DataRow(null, "table", null, new string[] { "id", "name" }, "ConvertToTable", DisplayName = "Source KeyFields with Table")]
        [DataRow(null, null, null, new string[] { "id", "name" }, "ConvertToDefaultType", DisplayName = "Source KeyFields with SourceType not provided")]
        public void TestUpdateSourceStringToDatabaseSourceObject(
            string? source,
            string? sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields,
            string task
        )
        {

            UpdateOptions options = new(
                source: source,
                permissions: new string[] { "anonymous", "*" },
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
                config: _testRuntimeConfig
            );

            string? actualConfig = AddPropertiesToJson(GetInitialConfiguration, GetBasicEntityWithAnonymousRole);
            string? expectedConfiguration;
            switch (task)
            {
                case "UpdateSourceName":
                    actualConfig = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntity);
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetBasicEntityWithAnonymousRole);
                    break;
                case "ConvertToStoredProcedure":
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntityWithSourceAsStoredProcedure);
                    break;
                case "ConvertToView":
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntityWithSourceForView);
                    break;
                default:
                    expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntityWithSourceWithDefaultType);
                    break;
            }

            Assert.IsTrue(TryUpdateExistingEntity(options, ref actualConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfiguration!), JObject.Parse(actualConfig)));
        }

        /// <summary>
        /// Simple test to verify success on updating a source from string to source object for valid fields.
        /// </summary>
        [DataTestMethod]
        [DataRow("newSourceName", null, null, "UpdateSourceName", DisplayName = "Update Source Name of the source object.")]
        [DataRow(null, new string[] { "param1:dab", "param2:false" }, null, "UpdateParameters", DisplayName = "update Parameters of stored procedure.")]
        [DataRow(null, null, new string[] { "col1", "col2" }, "UpdateKeyFields", DisplayName = "update KeyFields for table/view.")]
        public void TestUpdateDatabaseSourceObject(
            string? source,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields,
            string task
        )
        {

            UpdateOptions options = new(
                source: source,
                permissions: new string[] { "anonymous", "*" },
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
                config: _testRuntimeConfig
            );

            string? initialConfig = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntityWithSourceAsStoredProcedure);
            switch (task)
            {
                case "UpdateSourceName":
                    AssertOldAndUpdatedValuesForSourceObject(
                        options,
                        initialConfig,
                        "MyEntity",
                        "s001.book",
                        "newSourceName",
                        "stored-procedure",
                        "stored-procedure",
                        new Dictionary<string, object>() { { "param1", 123 }, { "param2", "hello" }, { "param3", true } },
                        new Dictionary<string, object>() { { "param1", 123 }, { "param2", "hello" }, { "param3", true } },
                        null,
                        null
                    );
                    break;

                case "UpdateParameters":
                    AssertOldAndUpdatedValuesForSourceObject(
                        options,
                        initialConfig,
                        "MyEntity",
                        "s001.book",
                        "s001.book",
                        "stored-procedure",
                        "stored-procedure",
                        new Dictionary<string, object>() { { "param1", 123 }, { "param2", "hello" }, { "param3", true } },
                        new Dictionary<string, object>() { { "param1", "dab" }, { "param2", false } },
                        null,
                        null
                    );
                    break;

                case "UpdateKeyFields":
                    initialConfig = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntityWithSourceWithDefaultType);
                    AssertOldAndUpdatedValuesForSourceObject(
                        options,
                        initialConfig,
                        "MyEntity",
                        "s001.book",
                        "s001.book",
                        "table",
                        "table",
                        null,
                        null,
                        new string[] { "id", "name" },
                        new string[] { "col1", "col2" }
                    );
                    break;
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
        private static void AssertOldAndUpdatedValuesForSourceObject(
            UpdateOptions options,
            string initialConfig,
            string entityName,
            string oldSourceName, string updatedSourceName,
            string oldSourceType, string updatedSourceType,
            Dictionary<string, object>? oldParameters, Dictionary<string, object>? updatedParameters,
            string[]? oldKeyFields, string[]? updatedKeyFields
        )
        {
            Entity entity = GetEntityObjectFromRuntimeConfigJson(initialConfig, entityName);
            entity.TryPopulateSourceFields();
            Assert.AreEqual(oldSourceName, entity.SourceName);
            Assert.AreEqual(oldSourceType, entity.SourceTypeName);
            Assert.AreEqual(
                ToAssertableStringFromDictionary(oldParameters),
                ToAssertableStringFromDictionary(entity.Parameters)
            );
            CollectionAssert.AreEquivalent(oldKeyFields, entity.KeyFields);
            Assert.IsTrue(TryUpdateExistingEntity(options, ref initialConfig));
            entity = GetEntityObjectFromRuntimeConfigJson(initialConfig, entityName);
            entity.TryPopulateSourceFields();
            Assert.AreEqual(updatedSourceName, entity.SourceName);
            Assert.AreEqual(updatedSourceType, entity.SourceTypeName);
            Assert.AreEqual(
                ToAssertableStringFromDictionary(updatedParameters),
                ToAssertableStringFromDictionary(entity.Parameters)
            );
            CollectionAssert.AreEquivalent(updatedKeyFields, entity.KeyFields);
        }

        /// <summary>
        /// Converts Dictionary into a string that can be Asserted for Testing.
        /// </summary>
        private static string? ToAssertableStringFromDictionary(Dictionary<string, object>? dictionary)
        {
            if (dictionary is null)
            {
                return null;
            }

            IEnumerable<string> pairStrings = dictionary.OrderBy(p => p.Key)
                .Select(p => p.Key + ":" + p.Value);
            return string.Join("; ", pairStrings);
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
                config: _testRuntimeConfig
            );

            string? actualConfig = AddPropertiesToJson(GetInitialConfiguration, GetEntityConfigurationWithPolicyAndFields);
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
            string? expectedConfiguration = AddPropertiesToJson(GetInitialConfiguration, updatedEntityConfigurationWithPolicyAndFields);
            Assert.IsTrue(TryUpdateExistingEntity(options, ref actualConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfiguration!), JObject.Parse(actualConfig)));
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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
        [DataRow(null, new string[] { "param1:value1" }, new string[] { "col1", "col2" }, DisplayName = "Both KeyFields and Parameters provided for source.")]
        [DataRow("stored-procedure", null, new string[] { "col1", "col2" }, DisplayName = "KeyFields with stored procedure.")]
        [DataRow("view", new string[] { "param1:value1" }, null, DisplayName = "Source Parameters with View")]
        [DataRow("table", new string[] { "param1:value1" }, null, DisplayName = "Source Parameters with Table")]
        [DataRow("table-view", new string[] { "param1:value1" }, null, DisplayName = "Invalid Source Type.")]
        public void TestAddNewEntityWithSourceObjectForInvalidFields(
            string? sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields
        )
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: new string[] { "anonymous", "*,create,read" },
                entity: "MyEntity",
                sourceType: sourceType,
                sourceParameters: parameters,
                sourceKeyFields: keyFields,
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
                config: _testRuntimeConfig);

            string runtimeConfig = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntityWithSourceAsStoredProcedure);

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
                config: _testRuntimeConfig);

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
                config: _testRuntimeConfig);

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
        /// Test to validate that Permissions is madatory when using options --fields.include or --fields.exclude
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
                config: _testRuntimeConfig
            );

            string runtimeConfig = GetConfigWithMappings();
            Assert.IsFalse(ConfigGenerator.TryUpdateExistingEntity(options, ref runtimeConfig));
        }

        /// <summary>
        /// Test to verify Invalid inputs to create a relationship
        /// </summary>
        [DataTestMethod]
        [DataRow("cosmos", "one", "MyEntity", DisplayName = "CosmosDb does not support relationships")]
        [DataRow("mssql", null, "MyEntity", DisplayName = "Cardinality should not be null")]
        [DataRow("mssql", "manyx", "MyEntity", DisplayName = "Cardinality should be one/many")]
        [DataRow("mssql", "one", null, DisplayName = "Target entity should not be null")]
        [DataRow("mssql", "one", "InvalidEntity", DisplayName = "Target Entity should be present in config to create a relationship")]
        public void TestVerifyCanUpdateRelationshipInvalidOptions(string db, string cardinality, string targetEntity)
        {
            RuntimeConfig runtimeConfig = new(
                Schema: "schema",
                DataSource: new DataSource(Enum.Parse<DatabaseType>(db)),
                CosmosDb: null,
                MsSql: null,
                PostgreSql: null,
                MySql: null,
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
                CosmosDb: null,
                MsSql: null,
                PostgreSql: null,
                MySql: null,
                RuntimeSettings: new Dictionary<GlobalSettingsType, object>(),
                Entities: entityMap
            );

            Assert.IsFalse(VerifyCanUpdateRelationship(runtimeConfig, cardinality: "one", targetEntity: "SampleEntity2"));
        }

        #endregion

        private static string GetInitialConfigString()
        {
            return @"
                        {
                        ""$schema"": ""dab.draft-01.schema.json"",
                        ""data-source"": {
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

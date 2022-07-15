namespace Hawaii.Cli.Tests
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
                permissions: "anonymous:create",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: "id,rating",
                fieldsToExclude: "level",
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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

            string expectedConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            {
                                                ""action"": ""create"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""rating""],
                                                    ""exclude"": [""level""]
                                                }
                                            },
                                            ""read"",
                                            ""update""
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
        /// Simple test to update an entity permission by creating a new role.
        /// Initially the role "authenticated" was not present, so it will create a new role.
        /// </summary>
        [TestMethod, Description("it should update the permission by adding a new role.")]
        public void TestUpdateEntityPermissionByAddingNewRole()
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: "authenticated:*",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: "id,rating",
                fieldsToExclude: "level",
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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

            string expectedConfig = GetInitialConfigString() + "," + @"
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
                permissions: "anonymous:update",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: "id,rating",
                fieldsToExclude: "level",
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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

            string expectedConfig = GetInitialConfigString() + "," + @"
                        ""entities"": {
                            ""MyEntity"": {
                                ""source"": ""MyTable"",
                                ""permissions"": [
                                    {
                                        ""role"": ""anonymous"",
                                        ""actions"": [
                                            {
                                                ""action"": ""update"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""rating""],
                                                    ""exclude"": [""level""]
                                                }
                                            },
                                            ""read""
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
                permissions: "anonymous:read,delete",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: "id,type,quantity",
                fieldsToExclude: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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
                                                ""action"": ""read"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""type"", ""quantity""],
                                                    ""exclude"": []
                                                }
                                            },
                                            {
                                                ""action"": ""delete"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""type"", ""quantity""],
                                                    ""exclude"": []
                                                }
                                            },
                                            {
                                                ""action"": ""create"",
                                                ""fields"": {
                                                    ""include"": [""id"", ""rating""],
                                                    ""exclude"": [""level""]
                                                }
                                            },
                                            {
                                                ""action"": ""update"",
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
                permissions: "anonymous:*",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: "id,rating",
                fieldsToExclude: "level",
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: "r2",
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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
        /// it will add source.fiels, target.fields, linking.object, linking.source.fields, linking.target.fields
        /// </summary>
        [TestMethod, Description("it should update an existing relationship")]
        public void TestUpdateEntityByModifyingRelationship()
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "SecondEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: "r2",
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: "eid1",
                linkingTargetFields: "eid2,fid2",
                mappingFields: "e1:e2,t2",
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: null,
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: "eid1",
                linkingTargetFields: "eid2,fid2",
                mappingFields: "e1:e2,t2",
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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
        /// Update Entity with new Policy and Field properties
        /// </summary>
        [DataTestMethod]
        [DataRow("*", "level,rating", "@claims.name eq 'hawaii'", "@claims.id eq @item.id", "PolicyAndFields", DisplayName = "Check adding new Policy and Fields to Action")]
        [DataRow(null, null, "@claims.name eq 'hawaii'", "@claims.id eq @item.id", "Policy", DisplayName = "Check adding new Policy to Action")]
        [DataRow("*", "level,rating", null, null, "Fields", DisplayName = "Check adding new fieldsToInclude and FieldsToExclude to Action")]
        public void TestUpdateEntityWithPolicyAndFieldProperties(string? fieldsToInclude,
                                                            string? fieldsToExclude,
                                                            string? policyRequest,
                                                            string? policyDatabase,
                                                            string check)
        {

            UpdateOptions options = new(
               source: "MyTable",
               permissions: "anonymous:delete",
                entity: "MyEntity",
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
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                map: null,
                name: "outputfile"
            );

            string? actualConfig = AddPropertiesToJson(GetInitialConfiguration, GetSingleEntity);
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

            Assert.IsTrue(TryUpdateExistingEntity(options, ref actualConfig));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfiguration!), JObject.Parse(actualConfig)));
        }

        /// <summary>
        /// Update Policy for an action
        /// </summary>
        [TestMethod]
        public void TestUpdatePolicy()
        {
            UpdateOptions options = new(
               source: "MyTable",
               permissions: "anonymous:delete",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                policyRequest: "@claims.name eq 'api_builder'",
                policyDatabase: "@claims.name eq @item.name",
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                map: null,
                name: "outputfile"
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
                                    ""action"": ""delete"",
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
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                map: new string[] { "id:Identity", "name:Company Name" },
                name: "outputfile");

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

            string expectedConfig = GetInitialConfigString() + "," + @"
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
        /// Test to Update existing mappings of an entity
        /// </summary>
        [TestMethod]
        public void TestUpdateExistingMappings()
        {
            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
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
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                map: new string[] { "name:Company Name", "addr:Company Address", "number:Contact Details" },
                name: "outputfile");

            string runtimeConfig = GetConfigWithMappings();

            string expectedConfig = GetInitialConfigString() + "," + @"
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
        /// example "*,read,create"
        /// update including WILDCARD along with other crud operation is not allowed
        /// </summary>
        [TestMethod, Description("update action should fail because of invalid action combination.")]
        public void TestUpdateEntityPermissionWithWildcardAndOtherCRUDAction()
        {
            UpdateOptions options = new(
                source: "MyTable",
                permissions: "anonymous:*,create,read",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: "id,rating",
                fieldsToExclude: "level",
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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
        /// Test to check failure on invalid permission string
        /// </summary>
        [TestMethod]
        public void TestParsingFromInvalidPermissionString()
        {
            string? role, actions;
            string permissions = "anonymous,create"; //wrong format
            bool isSuccess = TryGetRoleAndActionFromPermissionString(permissions, out role, out actions);

            Assert.IsFalse(isSuccess);
            Assert.IsNull(role);
            Assert.IsNull(actions);
        }

        /// <summary>
        /// Test to check creation of a new relationship with Invalid Mapping fields
        /// </summary>
        [TestMethod]
        public void TestCreateNewRelationshipWithInvalidMappingFields()
        {

            UpdateOptions options = new(
                source: null,
                permissions: null,
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: null,
                cardinality: "many",
                targetEntity: "FirstEntity",
                linkingObject: "entity_link",
                linkingSourceFields: "eid1",
                linkingTargetFields: "eid2,fid2",
                mappingFields: "e1,e2,t2", // Invalid value. Correct format uses ':' to separate source and target fields
                policyRequest: null,
                policyDatabase: null,
                map: null,
                name: "outputfile");

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
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                map: mappings.Split(','),
                name: "outputfile");

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

        #endregion

        private static string GetInitialConfigString()
        {
            return @"
                        {
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
                                        ""actions"": [
                                            ""read"",
                                            ""update""
                                        ]
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

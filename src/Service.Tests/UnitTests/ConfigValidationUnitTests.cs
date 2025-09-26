// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.Authorization;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Sql;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static Azure.DataApiBuilder.Service.Tests.TestHelper;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Test class to perform semantic validations on the runtime config object. At this point,
    /// the tests focus on the permissions portion of the entities property within the RuntimeConfig object.
    /// </summary>
    [TestClass]
    public class ConfigValidationUnitTests
    {
        /// <summary>
        /// Test method to validate that an appropriate exception is thrown when database policy tries to reference a field
        /// which is not accessible.
        /// </summary>
        /// <param name="dbPolicy">Database policy under test.</param>
        [DataTestMethod]
        [DataRow("@claims.id eq @item.id", DisplayName = "Field id is not accessible")]
        [DataRow("@claims.user_email eq @item.email and @claims.user_name ne @item.name", DisplayName = "Field email is not accessible")]
        public void InaccessibleFieldRequestedByPolicy(string dbPolicy)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: EntityActionOperation.Create,
                includedCols: new HashSet<string> { "*" },
                excludedCols: new HashSet<string> { "id", "email" },
                databasePolicy: dbPolicy
            );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
            Assert.AreEqual("Not all the columns required by policy are accessible.", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test method to validate that only 1 CRUD operation is supported for stored procedure
        /// and every role has that same single operation.
        /// </summary>
        [DataTestMethod]
        [DataRow("anonymous", new string[] { "execute" }, null, null, true, false, DisplayName = "Stored-procedure with valid execute permission only")]
        [DataRow("anonymous", new string[] { "*" }, null, null, true, false, DisplayName = "Stored-procedure with valid wildcard permission only, which resolves to execute")]
        [DataRow("anonymous", new string[] { "execute", "read" }, null, null, false, false, DisplayName = "Invalidly define operation in excess of execute")]
        [DataRow("anonymous", new string[] { "create", "read" }, null, null, false, false, DisplayName = "Stored-procedure with create-read permission")]
        [DataRow("anonymous", new string[] { "update", "read" }, null, null, false, false, DisplayName = "Stored-procedure with update-read permission")]
        [DataRow("anonymous", new string[] { "delete", "read" }, null, null, false, false, DisplayName = "Stored-procedure with delete-read permission")]
        [DataRow("anonymous", new string[] { "create" }, null, null, false, false, DisplayName = "Stored-procedure with invalid create permission")]
        [DataRow("anonymous", new string[] { "read" }, null, null, false, false, DisplayName = "Stored-procedure with invalid read permission")]
        [DataRow("anonymous", new string[] { "update" }, null, null, false, false, DisplayName = "Stored-procedure with invalid update permission")]
        [DataRow("anonymous", new string[] { "delete" }, null, null, false, false, DisplayName = "Stored-procedure with invalid delete permission")]
        [DataRow("anonymous", new string[] { "update", "create" }, null, null, false, false, DisplayName = "Stored-procedure with update-create permission")]
        [DataRow("anonymous", new string[] { "delete", "read", "update" }, null, null, false, false, DisplayName = "Stored-procedure with delete-read-update permission")]
        [DataRow("anonymous", new string[] { "execute" }, "authenticated", new string[] { "execute" }, true, false, DisplayName = "Stored-procedure with valid execute permission on all roles")]
        [DataRow("anonymous", new string[] { "*" }, "authenticated", new string[] { "*" }, true, false, DisplayName = "Stored-procedure with valid wildcard permission on all roles, which resolves to execute")]
        [DataRow("anonymous", new string[] { "execute" }, "authenticated", new string[] { "create" }, false, true, DisplayName = "Stored-procedure with valid execute and invalid create permission")]
        public void InvalidCRUDForStoredProcedure(
            string role1,
            string[] operationsRole1,
            string role2,
            string[] operationsRole2,
            bool isValid,
            bool differentOperationDifferentRoleFailure)
        {
            List<EntityPermission> permissionSettings = new()
            {
                new(
                Role: role1,
                Actions: operationsRole1.Select(a => new EntityAction(EnumExtensions.Deserialize<EntityActionOperation>(a), null, new())).ToArray())
            };

            // Adding another role for the entity.
            if (role2 is not null && operationsRole2 is not null)
            {
                permissionSettings.Add(new(
                    Role: role2,
                    Actions: operationsRole2.Select(a => new EntityAction(EnumExtensions.Deserialize<EntityActionOperation>(a), null, new())).ToArray()));
            }

            EntitySource entitySource = new(
                    Type: EntitySourceType.StoredProcedure,
                    Object: "sourceName",
                    Parameters: null,
                    KeyFields: null
                );

            Entity testEntity = new(
                Source: entitySource,
                Rest: new(EntityRestOptions.DEFAULT_HTTP_VERBS_ENABLED_FOR_SP),
                GraphQL: new(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ENTITY + "s"),
                Permissions: permissionSettings.ToArray(),
                Relationships: null,
                Mappings: null
            );

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE
            ) with
            { Entities = new(new Dictionary<string, Entity>() { { AuthorizationHelpers.TEST_ENTITY, testEntity } }) };

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            try
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
                Assert.AreEqual(true, isValid);
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(false, isValid);
                Assert.AreEqual(expected: $"Invalid operation for Entity: {AuthorizationHelpers.TEST_ENTITY}. " +
                            $"Stored procedures can only be configured with the 'execute' operation.", actual: ex.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
        }

        /// <summary>
        /// Test method to validate that an appropriate exception is thrown when there is an invalid action
        /// supplied in the RuntimeConfig.
        /// </summary>
        /// <param name="dbPolicy">Database policy.</param>
        /// <param name="action">The action to be validated.</param>
        [DataTestMethod]
        [DataRow("@claims.id eq @item.col1", EntityActionOperation.Insert, DisplayName = "Invalid action Insert specified in config")]
        [DataRow("@claims.id eq @item.col2", EntityActionOperation.Upsert, DisplayName = "Invalid action Upsert specified in config")]
        [DataRow("@claims.id eq @item.col3", EntityActionOperation.UpsertIncremental, DisplayName = "Invalid action UpsertIncremental specified in config")]
        public void InvalidActionSpecifiedForARole(string dbPolicy, EntityActionOperation action)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: action,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: dbPolicy
                );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
            Assert.AreEqual($"action:{action} specified for entity:{AuthorizationHelpers.TEST_ENTITY}," +
                    $" role:{AuthorizationHelpers.TEST_ROLE} is not valid.", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test that permission configuration validation fails when a database policy
        /// is defined for the Create operation for mysql/postgresql and passes for mssql.
        /// </summary>
        /// <param name="dbPolicy">Database policy.</param>
        /// <param name="errorExpected">Whether an error is expected.</param>
        [DataTestMethod]
        [DataRow(DatabaseType.PostgreSQL, "1 eq @item.col1", true, DisplayName = "Database Policy defined for Create fails for PostgreSQL")]
        [DataRow(DatabaseType.PostgreSQL, null, false, DisplayName = "Database Policy set as null for Create passes on PostgreSQL.")]
        [DataRow(DatabaseType.PostgreSQL, "", false, DisplayName = "Database Policy left empty for Create passes for PostgreSQL.")]
        [DataRow(DatabaseType.PostgreSQL, " ", false, DisplayName = "Database Policy only whitespace for Create passes for PostgreSQL.")]
        [DataRow(DatabaseType.MySQL, "1 eq @item.col1", true, DisplayName = "Database Policy defined for Create fails for MySQL")]
        [DataRow(DatabaseType.MySQL, null, false, DisplayName = "Database Policy set as for Create passes for MySQL")]
        [DataRow(DatabaseType.MySQL, "", false, DisplayName = "Database Policy left empty for Create passes for MySQL")]
        [DataRow(DatabaseType.MySQL, " ", false, DisplayName = "Database Policy only whitespace for Create passes for MySQL")]
        [DataRow(DatabaseType.MSSQL, "2 eq @item.col3", false, DisplayName = "Database Policy defined for Create passes for MSSQL")]
        [DataRow(DatabaseType.DWSQL, "2 eq @item.col3", false, DisplayName = "Database Policy defined for Create passes for DWSQL")]
        public void AddDatabasePolicyToCreateOperation(DatabaseType dbType, string dbPolicy, bool errorExpected)
        {
            EntityActionOperation action = EntityActionOperation.Create;
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: action,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: dbPolicy,
                dbType: dbType
            );

            try
            {
                RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
                Assert.IsFalse(errorExpected, message: "Validation expected to have failed.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(errorExpected, message: "Validation expected to have passed.");
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
        }

        /// <summary>
        /// Test method to check that Exception is thrown when Target Entity used in relationship is not defined in the config.
        /// </summary>
        [TestMethod]
        public void TestAddingRelationshipWithInvalidTargetEntity()
        {
            Dictionary<string, EntityRelationship> relationshipMap = new();

            // Creating relationship with an Invalid entity in relationship
            EntityRelationship sampleRelationship = new(
                Cardinality: Cardinality.One,
                TargetEntity: "INVALID_ENTITY",
                SourceFields: null,
                TargetFields: null,
                LinkingObject: null,
                LinkingSourceFields: null,
                LinkingTargetFields: null
            );

            relationshipMap.Add("rname1", sampleRelationship);

            Entity sampleEntity1 = GetSampleEntityUsingSourceAndRelationshipMap(
                source: "TEST_SOURCE1",
                relationshipMap: relationshipMap,
                graphQLDetails: new("SampleEntity1", "rname1s", true)
            );

            Dictionary<string, Entity> entityMap = new()
            {
                { "SampleEntity1", sampleEntity1 }
            };

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap)
            );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Assert that expected exception is thrown. Entity used in relationship is Invalid
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidateRelationshipConfigCorrectness(runtimeConfig));
            Assert.AreEqual($"Entity: {sampleRelationship.TargetEntity} used for relationship is not defined in the config.", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        }

        /// <summary>
        /// Test method to check that Exception is thrown when Entity used in the relationship has GraphQL disabled.
        /// </summary>
        [TestMethod]
        public void TestAddingRelationshipWithDisabledGraphQL()
        {
            // creating entity with disabled graphQL
            Entity sampleEntity1 = GetSampleEntityUsingSourceAndRelationshipMap(
                source: "TEST_SOURCE1",
                relationshipMap: null,
                graphQLDetails: new("", "", false)
            );

            Dictionary<string, EntityRelationship> relationshipMap = new();

            EntityRelationship sampleRelationship = new(
                Cardinality: Cardinality.One,
                TargetEntity: "SampleEntity1",
                SourceFields: null,
                TargetFields: null,
                LinkingObject: null,
                LinkingSourceFields: null,
                LinkingTargetFields: null
            );

            relationshipMap.Add("rname1", sampleRelationship);

            // Adding relationshipMap to SampleEntity1 (which has GraphQL enabled)
            Entity sampleEntity2 = GetSampleEntityUsingSourceAndRelationshipMap(
                source: "TEST_SOURCE2",
                relationshipMap: relationshipMap,
                graphQLDetails: new("", "", true)
            );

            Dictionary<string, Entity> entityMap = new()
            {
                { "SampleEntity1", sampleEntity1 },
                { "SampleEntity2", sampleEntity2 }
            };

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap)
            );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Exception should be thrown as we cannot use an entity (with graphQL disabled) in a relationship.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidateRelationshipConfigCorrectness(runtimeConfig));
            Assert.AreEqual($"Entity: {sampleRelationship.TargetEntity} is disabled for GraphQL.", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        }

        /// <summary>
        /// Test method to check that an exception is thrown in a many-many relationship (LinkingObject was provided)
        /// while linkingSourceFields and sourceFields are null, or targetFields and linkingTargetFields are null,
        /// and also the relationship is not defined in the database through foreign keys on the missing side of
        /// fields in the config for the many-many relationship. That means if source and linking source fields are
        /// missing that the foreign key information does not exist in the database for source entity to linking object,
        /// and if target and linking target fields are missing that the foreign key information does not exist in the
        /// database for the target entity to linking object.
        /// Further verify that after adding said foreignKeyPair in the Database, no exception is thrown. This is because
        /// once we have that foreign key information we can complete that side of the many-many relationship
        /// from that foreign key.
        /// </summary>
        [DataRow(null, null, new string[] { "targetField" }, new string[] { "linkingTargetField" }, "SampleEntity1",
            DisplayName = "sourceFields and LinkingSourceFields are null")]
        [DataRow(new string[] { "sourceField" }, new string[] { "linkingSourceField" }, null, null, "SampleEntity2",
            DisplayName = "targetFields and LinkingTargetFields are null")]
        [DataTestMethod]
        public void TestRelationshipWithLinkingObjectNotHavingRequiredFields(
            string[] sourceFields,
            string[] linkingSourceFields,
            string[] targetFields,
            string[] linkingTargetFields,
            string relationshipEntity
        )
        {
            // Creating an EntityMap with two sample entity
            Dictionary<string, Entity> entityMap = GetSampleEntityMap(
                sourceEntity: "SampleEntity1",
                targetEntity: "SampleEntity2",
                sourceFields: sourceFields,
                targetFields: targetFields,
                linkingObject: "TEST_SOURCE_LINK",
                linkingSourceFields: linkingSourceFields,
                linkingTargetFields: linkingTargetFields
            );

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap)
            );

            // Mocking EntityToDatabaseObject
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader) { IsLateConfigured = true };
            RuntimeConfigValidator configValidator = new(provider, fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);
            Mock<ISqlMetadataProvider> _sqlMetadataProvider = new();

            Dictionary<string, DatabaseObject> mockDictionaryForEntityDatabaseObject = new()
            {
                {
                    "SampleEntity1",
                    new DatabaseTable("dbo", "TEST_SOURCE1")
                },

                {
                    "SampleEntity2",
                    new DatabaseTable("dbo", "TEST_SOURCE2")
                }
            };

            _sqlMetadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(mockDictionaryForEntityDatabaseObject);

            // To mock the schema name and dbObjectName for linkingObject
            _sqlMetadataProvider.Setup(x =>
                x.ParseSchemaAndDbTableName("TEST_SOURCE_LINK")).Returns(("dbo", "TEST_SOURCE_LINK"));

            string discard;
            _sqlMetadataProvider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out discard)).Returns(true);

            Mock<IMetadataProviderFactory> _metadataProviderFactory = new();
            _metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(_sqlMetadataProvider.Object);

            // Exception thrown as foreignKeyPair not found in the DB.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidateRelationships(runtimeConfig, _metadataProviderFactory.Object));
            Assert.AreEqual($"Could not find relationship between Linking Object: TEST_SOURCE_LINK"
                + $" and entity: {relationshipEntity}.", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);

            // Mocking ForeignKeyPair to be defined In DB
            _sqlMetadataProvider.Setup(x =>
                x.VerifyForeignKeyExistsInDB(
                    new DatabaseTable("dbo", "TEST_SOURCE_LINK"), new DatabaseTable("dbo", "TEST_SOURCE1")
                )).Returns(true);

            _sqlMetadataProvider.Setup(x =>
                x.VerifyForeignKeyExistsInDB(
                    new DatabaseTable("dbo", "TEST_SOURCE_LINK"), new DatabaseTable("dbo", "TEST_SOURCE2")
                )).Returns(true);

            // Since, we have defined the relationship in Database,
            // the engine was able to find foreign key relation and validation will pass.
            configValidator.ValidateRelationships(runtimeConfig, _metadataProviderFactory.Object);
        }

        /// <summary>
        /// Test method to check that an exception is thrown when the relationship is one-many
        /// or many-one (determined by the linking object being null), while both SourceFields
        /// and TargetFields are null in the config, and the foreignKey pair between source and target
        /// is not defined in the database as well.
        /// Also verify that after adding foreignKeyPair between the source and target entities in the Database,
        /// no exception is thrown.
        /// </summary>
        [TestMethod]
        public void TestRelationshipWithNoLinkingObjectAndEitherSourceOrTargetFieldIsNull()
        {
            // Creating an EntityMap with two sample entity
            Dictionary<string, Entity> entityMap = GetSampleEntityMap(
                sourceEntity: "SampleEntity1",
                targetEntity: "SampleEntity2",
                sourceFields: null,
                targetFields: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null
            );

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap));

            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader) { IsLateConfigured = true };
            RuntimeConfigValidator configValidator = new(provider, fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);
            Mock<ISqlMetadataProvider> _sqlMetadataProvider = new();

            Dictionary<string, DatabaseObject> mockDictionaryForEntityDatabaseObject = new()
            {
                {
                    "SampleEntity1",
                    new DatabaseTable("dbo", "TEST_SOURCE1")
                },

                {
                    "SampleEntity2",
                    new DatabaseTable("dbo", "TEST_SOURCE2")
                }
            };

            _sqlMetadataProvider.Setup<Dictionary<string, DatabaseObject>>(x =>
                x.EntityToDatabaseObject).Returns(mockDictionaryForEntityDatabaseObject);

            Mock<IMetadataProviderFactory> _metadataProviderFactory = new();
            _metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(_sqlMetadataProvider.Object);

            // Exception is thrown as foreignKey pair is not specified in the config, nor defined
            // in the database.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidateRelationships(runtimeConfig, _metadataProviderFactory.Object));
            Assert.AreEqual($"Could not find relationship between entities:"
                + $" SampleEntity1 and SampleEntity2.", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);

            // Mocking ForeignKeyPair to be defined In DB
            _sqlMetadataProvider.Setup<bool>(x =>
                x.VerifyForeignKeyExistsInDB(
                    new DatabaseTable("dbo", "TEST_SOURCE1"), new DatabaseTable("dbo", "TEST_SOURCE2")
                )).Returns(true);

            _sqlMetadataProvider.Setup<bool>(x =>
                x.VerifyForeignKeyExistsInDB(
                    new DatabaseTable("dbo", "TEST_SOURCE2"), new DatabaseTable("dbo", "TEST_SOURCE1")
                )).Returns(true);

            _metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(_sqlMetadataProvider.Object);

            // No Exception is thrown as foreignKey Pair was found in the DB between
            // source and target entity.
            configValidator.ValidateRelationships(runtimeConfig, _metadataProviderFactory.Object);
        }

        /// <summary>
        /// Test method that ensures our validation code catches the cases where source and target fields do not match in some way
        /// and the linking object is null, indicating we have a one-many or many-one relationship.
        /// Not matching can either be because one is null and the other is not, or because they have a different number of fields.
        /// </summary>
        /// <param name="sourceFields">List of strings representing the source fields.</param>
        /// <param name="targetFields">List of strings representing the target fields.</param>
        /// <param name="expectedExceptionMessage">The error message we expect from validation.</param>
        [DataRow(new[] { "sourceFields" }, null, "Entity: SampleEntity1 has a relationship: rname1, which has source and target fields where one is null and the other is not.",
            DisplayName = "Linking object is null and sourceFields exist but targetFields are null.")]
        [DataRow(null, new[] { "targetFields" }, "Entity: SampleEntity1 has a relationship: rname1, which has source and target fields where one is null and the other is not.",
            DisplayName = "Linking object is null and targetFields exist but sourceFields are null")]
        [DataRow(new[] { "A", "B", "C" }, new[] { "1", "2" }, "Entity: SampleEntity1 has a relationship: rname1, which has 3 source fields defined, but 2 target fields defined.",
            DisplayName = "Linking object is null and sourceFields and targetFields have different length.")]
        [DataTestMethod]
        public void TestRelationshipWithoutSourceAndTargetFieldsMatching(
            string[] sourceFields,
            string[] targetFields,
            string expectedExceptionMessage)
        {
            // Creating an EntityMap with two sample entity
            Dictionary<string, Entity> entityMap = GetSampleEntityMap(
                sourceEntity: "SampleEntity1",
                targetEntity: "SampleEntity2",
                sourceFields: sourceFields,
                targetFields: targetFields,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null
            );

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap));

            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader) { IsLateConfigured = true };
            RuntimeConfigValidator configValidator = new(provider, fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);

            Dictionary<string, DatabaseObject> mockDictionaryForEntityDatabaseObject = new()
            {
                {
                    "SampleEntity1",
                    new DatabaseTable("dbo", "TEST_SOURCE1")
                },

                {
                    "SampleEntity2",
                    new DatabaseTable("dbo", "TEST_SOURCE2")
                }
            };

            // Exception is thrown since sourceFields and targetFields do not match in either their existence,
            // or their length.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidateRelationshipConfigCorrectness(runtimeConfig));
            Assert.AreEqual(expectedExceptionMessage, ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// This test method ensures that our validation code catches the case where the listed source or target fields
        /// are not valid backing columns in either the source or target entity respectively.
        /// </summary>
        /// <param name="sourceFields">List of strings representing the source fields.</param>
        /// <param name="targetFields">List of strings representing the target fields.</param>
        /// <param name="expectedExceptionMessage">The error message we expect from validation.</param>
        [DataRow(
            new[] { "noBackingColumn" },
            new[] { "backingColumn" },
            "Entity: SampleEntity1 has a relationship: rname1 with source fields: noBackingColumn " +
                "that do not exist as columns in entity: SampleEntity1.",
            DisplayName = "sourceField does not exist as valid backing column in source entity.")]
        [DataRow(
            new[] { "backingColumn" },
            new[] { "noBackingColumn" },
            "Entity: SampleEntity1 has a relationship: rname1 with target fields: noBackingColumn " +
                "that do not exist as columns in entity: SampleEntity2.",
            DisplayName = "targetField does not exist as valid backing column in target entity.")]
        [DataTestMethod]
        public void TestRelationshipWithoutSourceAndTargetFieldsAsValidBackingColumns(
            string[] sourceFields,
            string[] targetFields,
            string expectedExceptionMessage)
        {
            // Creating an EntityMap with two sample entity
            Dictionary<string, Entity> entityMap = GetSampleEntityMap(
                sourceEntity: "SampleEntity1",
                targetEntity: "SampleEntity2",
                sourceFields: sourceFields,
                targetFields: targetFields,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null
            );

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap));

            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader) { IsLateConfigured = true };
            RuntimeConfigValidator configValidator = new(provider, fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);
            Mock<ISqlMetadataProvider> _sqlMetadataProvider = new();

            Dictionary<string, DatabaseObject> mockDictionaryForEntityDatabaseObject = new()
            {
                {
                    "SampleEntity1",
                    new DatabaseTable("dbo", "TEST_SOURCE1")
                },

                {
                    "SampleEntity2",
                    new DatabaseTable("dbo", "TEST_SOURCE2")
                }
            };

            _sqlMetadataProvider.Setup<Dictionary<string, DatabaseObject>>(x =>
                x.EntityToDatabaseObject).Returns(mockDictionaryForEntityDatabaseObject);
            string discard;
            _sqlMetadataProvider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), "noBackingColumn", out discard)).Returns(false);
            _sqlMetadataProvider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), "backingColumn", out discard)).Returns(true);

            Mock<IMetadataProviderFactory> _metadataProviderFactory = new();
            _metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(_sqlMetadataProvider.Object);

            // Exception is thrown since either source or target field does not exist as a valid backing column in their respective entity.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidateRelationships(runtimeConfig, _metadataProviderFactory.Object));
            Assert.AreEqual(expectedExceptionMessage, ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test method that ensures our validation code catches the cases where we have a linking object and therefore a
        /// many-many relationship, and source and linking source or target and linking target fields do not match in some way.
        /// This can either be because one is null and the other is not, or because they have a different number of fields.
        /// </summary>
        /// <param name="sourceFields">List of strings representing the source fields.</param>
        /// <param name="linkingSourceFields">List of strings representing the linking source fields.</param>
        /// <param name="targetFields">List of strings representing the target fields.</param>
        /// <param name="linkingTargetFields">List of strings representing the linking target fields.</param>
        /// <param name="relationshipEntity">The name of the entity in the relationship.</param>
        /// <param name="expectedExceptionMessage">The expected error message.</param>
        [DataRow(
            null,
            new string[] { "linkingSourceFields" },
            new string[] { "targetFields" },
            new string[] { "linkingTargetFields" },
            "SampleEntity2",
            "Entity: SampleEntity1 has a many-many relationship: rname1, which has source and associated linking " +
                "fields where one is null and the other is not.",
            DisplayName = "Linking source fields are non null, but source fields are null in a many-many relationship.")]
        [DataRow(
            new string[] { "sourceFields" },
            null,
            new string[] { "targetFields" },
            new string[] { "linkingTargetFields" },
            "SampleEntity2",
            "Entity: SampleEntity1 has a many-many relationship: rname1, which has source and associated linking " +
                "fields where one is null and the other is not.",
            DisplayName = "Source fields are non null, but linking source fields are null in a many-many relationship.")]
        [DataRow(
            new string[] { "sourceField" },
            new string[] { "linkingSourceFields" },
            null,
            new string[] { "linkingTargetFields" },
            "SampleEntity2",
            "Entity: SampleEntity1 has a many-many relationship: rname1, which has target and associated linking " +
                "fields where one is null and the other is not.",
            DisplayName = "Linking target fields are non null, but target fields are null in a many-many relationship.")]
        [DataRow(
            new string[] { "sourceField" },
            new string[] { "linkingSourceFields" },
            new string[] { "targetFields" },
            null,
            "SampleEntity2",
            "Entity: SampleEntity1 has a many-many relationship: rname1, which has target and associated linking " +
                "fields where one is null and the other is not.",
            DisplayName = "Target fields are non null, but linking target fields are null in a many-many relationship.")]
        [DataRow(
            new string[] { "1", "2" },
            new string[] { "A", "B", "C" },
            new string[] { "targetFields" },
            new string[] { "linkingTargetFields" },
            "SampleEntity2",
            "Entity: SampleEntity1 has a many-many relationship: rname1 with 2 source fields defined, but 3 linking source fields defined.",
            DisplayName = "Source fields and linking source fields are different length in a many-many relationship.")]
        [DataRow(
            new string[] { "sourceFields" },
            new string[] { "linkingSourceFields" },
            new string[] { "A", "B", "C" },
            new string[] { "1", "2" },
            "SampleEntity2",
            "Entity: SampleEntity1 has a many-many relationship: rname1 with 3 target fields defined, but 2 linking target fields defined.",
            DisplayName = "Target fields and linking target fields are different length in a many-many relationship.")]
        [DataTestMethod]
        public void TestRelationshipWithoutLinkingSourceAndTargetFieldsMatching(
            string[] sourceFields,
            string[] linkingSourceFields,
            string[] targetFields,
            string[] linkingTargetFields,
            string relationshipEntity,
            string expectedExceptionMessage)
        {
            // Creating an EntityMap with two sample entity
            Dictionary<string, Entity> entityMap = GetSampleEntityMap(
                sourceEntity: "SampleEntity1",
                targetEntity: "SampleEntity2",
                sourceFields: sourceFields,
                targetFields: targetFields,
                linkingObject: "TEST_SOURCE_LINK",
                linkingSourceFields: linkingSourceFields,
                linkingTargetFields: linkingTargetFields
            );

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap));

            // Mocking EntityToDatabaseObject
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader) { IsLateConfigured = true };
            RuntimeConfigValidator configValidator = new(provider, fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);

            Dictionary<string, DatabaseObject> mockDictionaryForEntityDatabaseObject = new()
            {
                {
                    "SampleEntity1",
                    new DatabaseTable("dbo", "TEST_SOURCE1")
                },

                {
                    "SampleEntity2",
                    new DatabaseTable("dbo", "TEST_SOURCE2")
                }
            };

            // Exception is thrown since linkingSourceFields and linkingTargetFields do not match in either their existence,
            // or their length.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidateRelationshipConfigCorrectness(runtimeConfig));
            Assert.AreEqual(expectedExceptionMessage, ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test method to validate that an appropriate exception is thrown when there is
        /// one or more empty claimtypes specified in the database policy.
        /// </summary>
        /// <param name="policy"></param>
        [DataTestMethod]
        [DataRow("@claims. eq @item.col1", DisplayName = "Empty claim type test 1")]
        [DataRow("@claims. ne @item.col2", DisplayName = "Empty claim type test 2")]
        public void EmptyClaimTypeSuppliedInPolicy(string dbPolicy)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: EntityActionOperation.Create,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: dbPolicy
                );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidatePermissionsInConfig(runtimeConfig));
            Assert.AreEqual("ClaimType cannot be empty.", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test to validate that we are correctly throwing an appropriate exception when the database policy
        /// contains one or more claims with invalid format.
        /// </summary>
        /// <param name="policy">The policy to be parsed.</param>
        [DataTestMethod]
        [DataRow("@claims.user_email eq @item.col1 and @claims.emp/rating eq @item.col2", DisplayName = "/ in claimType")]
        [DataRow("@claims.user$email eq @item.col1 and @claims.emp_rating eq @item.col2", DisplayName = "$ in claimType")]
        [DataRow("@claims.user_email eq @item.col1 and not ( true eq @claims.isemp%loyee or @claims.name eq 'Aaron')"
            , DisplayName = "% in claimType")]
        [DataRow("@claims.user+email eq @item.col1 and @claims.isemployee eq @item.col2", DisplayName = "+ in claimType")]
        [DataRow("@claims.(use(r) eq @item.col1 and @claims.isemployee eq @item.col2", DisplayName = "Parenthesis in claimType 1")]
        [DataRow("@claims.(user_email) eq @item.col1 and @claims.isemployee eq @item.col2", DisplayName = "Parenthesis in claimType 2")]
        public void ParseInvalidDbPolicyWithInvalidClaimTypeFormat(string policy)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: EntityActionOperation.Create,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: policy
                );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidatePermissionsInConfig(runtimeConfig));
            Assert.IsTrue(ex.Message.StartsWith("Invalid format for claim type"));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test to validate that only and only for SWA, if claims other than "userId" and
        /// "userDetails" are referenced in the database policy, we fail the validation.
        /// </summary>
        /// <param name="authProvider">Authentication provider like AppService, StaticWebApps.</param>
        /// <param name="dbPolicy">Database policy defined for action.</param>
        /// <param name="action">The action for which database policy is defined.</param>
        /// <param name="errorExpected">Boolean value indicating whether an exception is expected or not.</param>
        [DataTestMethod]
        [DataRow("StaticWebApps", "@claims.userId eq @item.col2", EntityActionOperation.Read, false, DisplayName = "SWA- Database Policy defined for Read passes")]
        [DataRow("staticwebapps", "@claims.userDetails eq @item.col3", EntityActionOperation.Update, false, DisplayName = "SWA- Database Policy defined for Update passes")]
        [DataRow("StaticWebAPPs", "@claims.email eq @item.col3", EntityActionOperation.Delete, true, DisplayName = "SWA- Database Policy defined for Delete fails")]
        [DataRow("appService", "@claims.email eq @item.col3", EntityActionOperation.Delete, false, DisplayName = "AppService- Database Policy defined for Delete passes")]
        public void TestInvalidClaimsForStaticWebApps(string authProvider, string dbPolicy, EntityActionOperation action, bool errorExpected)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: action,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: dbPolicy,
                authProvider: authProvider.ToString()
                );
            try
            {
                RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
                Assert.IsFalse(errorExpected);
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(errorExpected);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
                Assert.AreEqual(RuntimeConfigValidator.INVALID_CLAIMS_IN_POLICY_ERR_MSG, ex.Message);
            }
        }

        /// <summary>
        /// Test to validate that wildcard action passes all stages of config validation.
        /// </summary>
        [TestMethod]
        public void WildcardActionSpecifiedForARole()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: EntityActionOperation.All,
                includedCols: new HashSet<string> { "col1", "col2", "col3" }
                );

            // All the validations would pass, and no exception would be thrown.
            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();
            configValidator.ValidatePermissionsInConfig(runtimeConfig);
        }

        /// <summary>
        /// Test to validate that no other field can be present in included set if wildcard is present
        /// in it.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntityActionOperation.Create, DisplayName = "Wildcard Field with another field in included set and create action")]
        [DataRow(EntityActionOperation.Update, DisplayName = "Wildcard Field with another field in included set and update action")]
        public void WildCardAndOtherFieldsPresentInIncludeSet(EntityActionOperation actionOp)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: actionOp,
                includedCols: new HashSet<string> { "*", "col2" }
                );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidatePermissionsInConfig(runtimeConfig));
            string actionName = actionOp.ToString();
            Assert.AreEqual($"No other field can be present with wildcard in the included set for: entity:{AuthorizationHelpers.TEST_ENTITY}," +
                $" role:{AuthorizationHelpers.TEST_ROLE}, action:{actionName}", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [DataTestMethod]
        [DataRow(EntityActionOperation.Create, DisplayName = "Wildcard Field with another field in excluded set and create action")]
        [DataRow(EntityActionOperation.Update, DisplayName = "Wildcard Field with another field in excluded set and update action")]
        public void WildCardAndOtherFieldsPresentInExcludeSet(EntityActionOperation actionOp)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: actionOp,
                excludedCols: new HashSet<string> { "*", "col1" }
                );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidatePermissionsInConfig(runtimeConfig));
            string actionName = actionOp.ToString();
            Assert.AreEqual($"No other field can be present with wildcard in the excluded set for: entity:{AuthorizationHelpers.TEST_ENTITY}," +
                $" role:{AuthorizationHelpers.TEST_ROLE}, action:{actionName}", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test to validate that differently cased operation names specified in config are deserialised correctly,
        /// and hence they pass config validation stage if they are allowed CRUD operation and fail otherwise.
        /// </summary>
        /// <param name="operationName">Name of the operation configured.</param>
        /// <param name="exceptionExpected">Boolean variable which indicates whether the relevant method call
        /// is expected to return an exception.</param>
        [DataTestMethod]
        [DataRow("CREATE", false, DisplayName = "Valid operation name CREATE specified for action")]
        [DataRow("rEAd", false, DisplayName = "Valid operation name rEAd specified for action")]
        [DataRow("UPDate", false, DisplayName = "Valid operation name UPDate specified for action")]
        [DataRow("DelETe", false, DisplayName = "Valid operation name DelETe specified for action")]
        [DataRow("inseRt", true, DisplayName = "Invalid operation name inseRt specified for action")]
        public void TestOperationValidityAndCasing(string operationName, bool exceptionExpected)
        {
            string actionJson = @"{
                                        ""action"": " + $"\"{operationName}\"" + @",
                                        ""policy"": {
                                            ""database"": null
                                          },
                                        ""fields"": {
                                            ""include"": [""*""]
                                          }
                                  }";
            object actionForRole = JsonSerializer.Deserialize<object>(actionJson);

            EntityPermission permissionForEntity = new(
                Role: AuthorizationHelpers.TEST_ROLE,
                Actions: new[] {
                    new EntityAction(
                        EnumExtensions.Deserialize<EntityActionOperation>(operationName),
                        new(Exclude: new(), Include: new() { "*" }),
                        new())
                });

            Entity sampleEntity = new(
                Source: new(AuthorizationHelpers.TEST_ENTITY, EntitySourceType.Table, null, null),
                Rest: null,
                GraphQL: null,
                Permissions: new[] { permissionForEntity },
                Relationships: null,
                Mappings: null
                );

            Dictionary<string, Entity> entityMap = new()
            {
                { AuthorizationHelpers.TEST_ENTITY, sampleEntity }
            };

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap));

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            if (!exceptionExpected)
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
            else
            {
                DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidatePermissionsInConfig(runtimeConfig));

                // Assert that the exception returned is the one we expected.
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
                Assert.AreEqual(
                    $"action:{operationName} specified for entity:{AuthorizationHelpers.TEST_ENTITY}, role:{AuthorizationHelpers.TEST_ROLE} is not valid.",
                    ex.Message,
                    ignoreCase: true);
            }
        }

        /// <summary>
        /// Test that entity names from config successfully pass or fail validation.
        /// When a failure is expected: Asserts that an exception is thrown, and with that exception object,
        /// validates the exception's status and substatus codes.
        /// </summary>
        /// <param name="entityNameFromConfig"></param>
        [DataTestMethod]
        [DataRow("entityname", false, DisplayName = "Valid lower case letter as first character")]
        [DataRow("Entityname", false, DisplayName = "Valid upper case letter as first character")]
        [DataRow("Entity_name", false, DisplayName = "Valid _ in body")]
        [DataRow("@entityname", true, DisplayName = "Invalid start character @")]
        [DataRow("_entityname", true, DisplayName = "Invalid start character _")]
        [DataRow("#entityname", true, DisplayName = "Invalid start character #")]
        [DataRow("5Entityname", true, DisplayName = "Invalid start character 5")]
        [DataRow("E.ntityName", true, DisplayName = "Invalid body character .")]
        [DataRow("Entity^Name", true, DisplayName = "Invalid body character ^")]
        [DataRow("Entity&Name", true, DisplayName = "Invalid body character &")]
        [DataRow("Entity name", true, DisplayName = "Invalid body character whitespace")]
        [DataRow("__introspectionField", true, DisplayName = "Invalid double underscore reserved for introspection fields.")]
        public void ValidateGraphQLTypeNamesFromConfig(string entityNameFromConfig, bool expectsException)
        {
            Dictionary<string, Entity> entityMap = new();

            // Sets only the top level name and enables GraphQL for entity
            Entity entity = SchemaConverterTests.GenerateEmptyEntity("");
            entity = entity with { GraphQL = entity.GraphQL with { Enabled = true } };
            entityMap.Add(entityNameFromConfig, entity);

            // Sets the top level name to an arbitrary value since it is not used in this check
            // and enables GraphQL for entity by setting the GraphQLSettings.Type to a string.
            entity = SchemaConverterTests.GenerateEmptyEntity("");
            entity = entity with { GraphQL = new(Singular: entityNameFromConfig, Plural: "") };
            entityMap.Add("EntityA", entity);

            // Sets the top level name to an arbitrary value since it is not used in this check
            // and enables GraphQL for entity by setting the GraphQLSettings.Type to
            // a SingularPlural object where both Singular and Plural are defined.
            entity = SchemaConverterTests.GenerateEmptyEntity("");
            entity = entity with { GraphQL = new(entityNameFromConfig, entityNameFromConfig) };
            entityMap.Add("EntityC", entity);

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, string.Empty, Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap)
                );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            if (expectsException)
            {
                DataApiBuilderException dabException = Assert.ThrowsException<DataApiBuilderException>(
                    action: () => configValidator.ValidateEntityConfiguration(runtimeConfig),
                    message: $"Entity name \"{entityNameFromConfig}\" incorrectly passed validation.");

                Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: dabException.StatusCode);
                Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: dabException.SubStatusCode);
            }
            else
            {
                configValidator.ValidateEntityConfiguration(runtimeConfig);
            }
        }

        /// <summary>
        /// Validates that an exception is thrown when
        /// there is a collision in the graphQL queries
        /// generated by the entity definitions.
        /// This test declares entities with the following graphQL
        /// definitions
        /// "book" {
        ///     "graphQL": true
        /// }
        /// "Book" {
        ///     "graphQL": true
        /// }
        /// </summary>
        [TestMethod]
        [DataRow(DatabaseType.MySQL)] // Relational Database
        [DataRow(DatabaseType.CosmosDB_NoSQL)] // Non Relational Database
        public void ValidateEntitiesWithGraphQLExposedGenerateDuplicateQueries(DatabaseType databaseType)
        {
            // Entity Name: Book
            // pk_query: book_by_pk
            // List Query: books
            Entity bookWithUpperCase = GraphQLTestHelpers.GenerateEmptyEntity() with { GraphQL = new("book", "books") };

            // Entity Name: book
            // pk_query: book_by_pk
            // List Query: books
            Entity book = GraphQLTestHelpers.GenerateEmptyEntity() with { GraphQL = new("Book", "Books") };

            SortedDictionary<string, Entity> entityCollection = new()
            {
                { "book", book },
                { "Book", bookWithUpperCase }
            };
            ValidateExceptionForDuplicateQueriesDueToEntityDefinitions(entityCollection, "Book", databaseType);
        }

        /// <summary>
        /// Validates that an exception is thrown when
        /// there is a collision in the graphQL queries
        /// generated by the entity definitions.
        /// This test declares entities with the following graphQL
        /// definitions
        /// "ExecuteBook" {
        ///     "source" :{
        ///         "type": "table"
        ///    }
        ///     "graphQL": true
        /// }
        /// "Book_by_pk" {
        ///     "source" :{
        ///         "type": "stored-procedure"
        ///    }
        ///     "graphQL": true
        /// }
        /// </summary>
        [TestMethod]
        [DataRow(DatabaseType.MySQL)] // Relational Database
        [DataRow(DatabaseType.CosmosDB_NoSQL)] // Non Relational Database
        public void ValidateStoredProcedureAndTableGeneratedDuplicateQueries(DatabaseType databaseType)
        {
            // Entity Name: ExecuteBook
            // Entity Type: table
            // pk_query: executebook_by_pk
            // List Query: executebooks
            Entity bookTable = GraphQLTestHelpers.GenerateEmptyEntity(sourceType: EntitySourceType.Table);

            // Entity Name: book_by_pk
            // Entity Type: Stored Procedure
            // StoredProcedure Query: executebook_by_pk
            Entity bookByPkStoredProcedure = GraphQLTestHelpers.GenerateEmptyEntity(sourceType: EntitySourceType.StoredProcedure);

            SortedDictionary<string, Entity> entityCollection = new()
            {
                { "executeBook", bookTable },
                { "Book_by_pk", bookByPkStoredProcedure }
            };
            ValidateExceptionForDuplicateQueriesDueToEntityDefinitions(entityCollection, "executeBook", databaseType);
        }

        /// <summary>
        /// Validates that an exception is thrown when
        /// there is a collision in the graphQL mutation
        /// generated by the entity definitions.
        /// This test declares entities with the following graphQL
        /// definitions
        /// "ExecuteBooks" {
        ///     "source" :{
        ///         "type": "table"
        ///    }
        ///     "graphQL": true
        /// }
        /// "AddBook" {
        ///     "source" :{
        ///         "type": "stored-procedure"
        ///    }
        ///     "graphQL": {
        ///         "type": "Books"
        ///     }
        /// }
        /// </summary>
        [TestMethod]
        [DataRow(DatabaseType.MySQL)] // Relational Database
        [DataRow(DatabaseType.CosmosDB_NoSQL)] // Non Relational Database
        public void ValidateStoredProcedureAndTableGeneratedDuplicateMutation(DatabaseType databaseType)
        {
            // Entity Name: Book
            // Entity Type: table
            // mutation generated: createBook, updateBook, deleteBook
            Entity bookTable = GraphQLTestHelpers.GenerateEmptyEntity(sourceType: EntitySourceType.Table);

            // Entity Name: AddBook
            // Entity Type: Stored Procedure
            // StoredProcedure mutation: createBook
            Entity addBookStoredProcedure = GraphQLTestHelpers.GenerateEntityWithStringType("Books", EntitySourceType.StoredProcedure);

            SortedDictionary<string, Entity> entityCollection = new()
            {
                { "ExecuteBooks", bookTable },
                { "AddBook", addBookStoredProcedure }
            };
            ValidateExceptionForDuplicateQueriesDueToEntityDefinitions(entityCollection, "ExecuteBooks", databaseType);
        }

        /// <summary>
        /// Validates that an exception is thrown when
        /// there is a collision in the graphQL queries
        /// generated by the entity definitions.
        /// This test declares entities with the following graphQL
        /// definitions
        /// "book" {
        ///     "graphQL": true
        /// }
        /// "book_alt" {
        ///     "graphQL": {
        ///         "type": "book"
        ///     }
        ///  }
        /// </summary>
        [TestMethod]
        [DataRow(DatabaseType.MySQL)] // Relational Database
        [DataRow(DatabaseType.CosmosDB_NoSQL)] // Non Relational Database
        public void ValidateEntitiesWithNameCollisionInGraphQLTypeGenerateDuplicateQueriesCase(DatabaseType databaseType)
        {
            // Entity Name: book
            // pk_query: book_by_pk
            // List Query: books
            Entity book = GraphQLTestHelpers.GenerateEmptyEntity();

            // Entity Name: book_alt
            // pk_query: book_by_pk
            // List Query: books
            Entity book_alt = GraphQLTestHelpers.GenerateEntityWithStringType("book");

            SortedDictionary<string, Entity> entityCollection = new()
            {
                { "book", book },
                { "book_alt", book_alt }
            };
            ValidateExceptionForDuplicateQueriesDueToEntityDefinitions(entityCollection, "book_alt", databaseType);
        }

        /// <summary>
        /// Validates that an exception is thrown when
        /// there is a collision in the graphQL queries
        /// generated by the entity definitions.
        /// This test declares entities with the following graphQL
        /// definitions
        /// "book" {
        ///     "graphQL": {
        ///         "type": {
        ///             "singular": "book",
        ///             "plural": "books",
        ///         }
        ///     }
        /// }
        /// "book_alt" {
        ///     "graphQL": {
        ///         "type": "book"
        ///     }
        ///  }
        /// </summary>
        [TestMethod]
        [DataRow(DatabaseType.MySQL)] // Relational Database
        [DataRow(DatabaseType.CosmosDB_NoSQL)] // Non Relational Database
        public void ValidateEntitiesWithCollisionsInSingularPluralNamesGenerateDuplicateQueries(DatabaseType databaseType)
        {
            // Entity Name: book
            // pk_query: book_by_pk
            // List Query: books
            Entity book = GraphQLTestHelpers.GenerateEntityWithSingularPlural("book", "books");

            // Entity Name: book_alt
            // pk_query: book_by_pk
            // List Query: books
            Entity book_alt = GraphQLTestHelpers.GenerateEntityWithStringType("book");

            SortedDictionary<string, Entity> entityCollection = new()
            {
                { "book", book },
                { "book_alt", book_alt }
            };
            ValidateExceptionForDuplicateQueriesDueToEntityDefinitions(entityCollection, "book_alt", databaseType);
        }

        /// <summary>
        /// Validates that an exception is thrown when there is a collision
        /// in the graphQL query names generated by the entity definitions.
        /// This test declares entities with the following graphQL definitions
        /// "book_alt"{
        ///     "graphQL": {
        ///         "type": {
        ///             "singular": "book",
        ///             "plural": "books"
        ///         }
        ///     }
        ///  }
        /// "book": {
        ///     "graphQL": true
        /// }
        /// </summary>
        [TestMethod]
        [DataRow(DatabaseType.MySQL)] // Relational Database
        [DataRow(DatabaseType.CosmosDB_NoSQL)] // Non Relational Database
        public void ValidateEntitiesWithNameCollisionInSingularPluralTypeGeneratesDuplicateQueries(DatabaseType databaseType)
        {
            SortedDictionary<string, Entity> entityCollection = new();

            // Entity Name: book_alt.
            // pk_query: book_by_pk
            // List query: books
            Entity book_alt = GraphQLTestHelpers.GenerateEntityWithSingularPlural("book", "books");

            // Entity Name: book
            // pk_query: book_by_pk
            // List Query: books
            Entity book = GraphQLTestHelpers.GenerateEmptyEntity();

            entityCollection.Add("book_alt", book_alt);
            entityCollection.Add("book", book);
            ValidateExceptionForDuplicateQueriesDueToEntityDefinitions(entityCollection, "book_alt", databaseType);
        }

        /// <summary>
        /// Validates that no exception is thrown when the entities
        /// exposed for graphQL generate unique queries.
        /// This test uses the following entity definitions
        /// "Book": {
        ///     "graphQL": false
        /// }
        /// "book": {
        ///     "graphQL": true
        /// }
        /// "book_alt"{
        ///     "graphQL": {
        ///         "type": {
        ///             "singular": "book_alt",
        ///             "plural": "books_alt"
        ///         }
        ///     }
        ///  }
        /// "Book_alt"{
        ///     "graphQL": {
        ///         "type": "book_alternative"
        ///     }
        /// }
        /// "BooK"{
        ///     graphQL: {
        ///         "type": {
        ///             "singular": "BooK",
        ///             "plural": "BooKs"
        ///         }
        ///     }
        ///  }
        /// "BOOK"{
        ///     graphQL: {
        ///         "type": {
        ///             "singular": "BOOK",
        ///             "plural": "BOOKS"
        ///         }
        ///     }
        ///  }
        /// </summary>
        [TestMethod]
        [DataRow(DatabaseType.MySQL)] // Relational Database
        [DataRow(DatabaseType.CosmosDB_NoSQL)] // Non Relational Database
        public void ValidateValidEntityDefinitionsDoesNotGenerateDuplicateQueries(DatabaseType databaseType)
        {
            // Entity Name: Book
            // GraphQL is not exposed for this entity
            Entity bookWithUpperCase = GraphQLTestHelpers.GenerateEmptyEntity() with { GraphQL = new("", "", false) };

            // Entity Name: book
            // pk query: book_by_pk
            // List query: books
            Entity book = GraphQLTestHelpers.GenerateEmptyEntity();

            // Entity Name: book_alt
            // pk_query: book_alt_by_pk
            // List query: books_alt
            Entity book_alt = GraphQLTestHelpers.GenerateEntityWithSingularPlural("book_alt", "books_alt");

            // Entity Name: Book_alt
            // pk_query: book_alternative
            // List query: books_alternatives
            Entity book_alt_upperCase = GraphQLTestHelpers.GenerateEntityWithStringType("book_alternative");

            // Entity Name: BooK
            // pk_query: booK_by_pk
            // List query: booKs
            Entity bookWithDifferentCase = GraphQLTestHelpers.GenerateEntityWithSingularPlural("BooK", "BooKs");

            // Entity Name: BOOK
            // pk_query: bOOK_by_pk
            // List query: bOOKS
            Entity bookWithAllUpperCase = GraphQLTestHelpers.GenerateEntityWithSingularPlural("BOOK", "BOOKS");

            SortedDictionary<string, Entity> entityCollection = new()
            {
                { "book", book },
                { "Book", bookWithUpperCase },
                { "book_alt", book_alt },
                { "BooK", bookWithDifferentCase },
                { "BOOK", bookWithAllUpperCase },
                { "Book_alt", book_alt_upperCase }
            };

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();
            configValidator.ValidateEntitiesDoNotGenerateDuplicateQueriesOrMutation(databaseType, new(entityCollection));
        }

        /// <summary>
        /// Tests whether conflicting global REST/GraphQL fail config validation.
        /// </summary>
        /// <param name="graphQLConfiguredPath">GraphQL global path</param>
        /// <param name="restConfiguredPath">REST global path</param>
        /// <param name="mcpConfiguredPath">MCP global path</param>
        /// <param name="expectError">Exception expected</param>
        [DataTestMethod]
        [DataRow("/graphql", "/graphql", "/mcp", true, DisplayName = "GraphQL and REST conflict (same path).")]
        [DataRow("/api", "/api", "/mcp", true, DisplayName = "REST and GraphQL conflict (same path).")]
        [DataRow("/graphql", "/api", "/mcp", false, DisplayName = "GraphQL, REST, and MCP distinct.")]
        // Extra case: conflict with MCP
        [DataRow("/mcp", "/api", "/mcp", true, DisplayName = "MCP and GraphQL conflict (same path).")]
        [DataRow("/graphql", "/mcp", "/mcp", true, DisplayName = "MCP and REST conflict (same path).")]
        public void TestGlobalRouteValidation(string graphQLConfiguredPath, string restConfiguredPath, string mcpConfiguredPath, bool expectError)
        {
            GraphQLRuntimeOptions graphQL = new(Path: graphQLConfiguredPath);
            RestRuntimeOptions rest = new(Path: restConfiguredPath);
            McpRuntimeOptions mcp = new(Path: mcpConfiguredPath);

            RuntimeConfig configuration = ConfigurationTests.InitMinimalRuntimeConfig(
                new(DatabaseType.MSSQL, "", Options: null),
                graphQL,
                rest,
                mcp);
            string expectedErrorMessage = "Conflicting path configuration between GraphQL, REST, and MCP.";

            try
            {
                RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();
                configValidator.ValidateGlobalEndpointRouteConfig(configuration);

                if (expectError)
                {
                    Assert.Fail(message: "Global endpoint route config validation expected to fail.");
                }
            }
            catch (DataApiBuilderException ex)
            {
                if (expectError)
                {
                    Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: ex.StatusCode);
                    Assert.AreEqual(expected: expectedErrorMessage, actual: ex.Message);
                }
                else
                {
                    Assert.Fail(message: "Global endpoint route config validation not expected to fail.");
                }
            }
        }

        /// <summary>
        /// A test helper method to validate the exception thrown when entities generate
        /// queries with the same name.
        /// </summary>
        /// <param name="entityCollection">Entity definitions</param>
        /// <param name="entityName">Entity name to construct the expected exception message</param>
        private static void ValidateExceptionForDuplicateQueriesDueToEntityDefinitions(SortedDictionary<string, Entity> entityCollection, string entityName, DatabaseType databaseType)
        {
            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();
            DataApiBuilderException dabException = Assert.ThrowsException<DataApiBuilderException>(
               action: () => configValidator.ValidateEntitiesDoNotGenerateDuplicateQueriesOrMutation(databaseType, new(entityCollection)));

            Assert.AreEqual(expected: $"Entity {entityName} generates queries/mutation that already exist", actual: dabException.Message);
            Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: dabException.StatusCode);
            Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: dabException.SubStatusCode);
        }

        /// <summary>
        /// Method to create a sample entity with GraphQL enabled,
        /// with given source and relationship Info.
        /// Rest is disabled by default, unless specified otherwise.
        /// </summary>
        /// <param name="source">Database name of entity.</param>
        /// <param name="relationshipMap">Dictionary containing {relationshipName, Relationship}</param>
        private static Entity GetSampleEntityUsingSourceAndRelationshipMap(
            string source,
            Dictionary<string, EntityRelationship> relationshipMap,
            EntityGraphQLOptions graphQLDetails,
            EntityRestOptions restDetails = null
            )
        {
            EntityAction actionForRole = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: null);

            EntityPermission permissionForEntity = new(
                Role: "anonymous",
                Actions: new[] { actionForRole });

            Entity sampleEntity = new(
                Source: new(source, EntitySourceType.Table, null, null),
                Rest: restDetails ?? new(Enabled: false),
                GraphQL: graphQLDetails,
                Permissions: new[] { permissionForEntity },
                Relationships: relationshipMap,
                Mappings: null
                );

            return sampleEntity;
        }

        /// <summary>
        /// Returns Dictionary containing pair of string and entity.
        /// It creates two sample entities and forms relationship between them.
        /// </summary>
        /// <param name="sourceEntity">Name of the source entity.</param>
        /// <param name="targetEntity">Name of the target entity.</param>
        /// <param name="sourceFields">List of strings representing the source field names.</param>
        /// <param name="targetFields">List of strings representing the target field names.</param>
        /// <param name="linkingObject">Name of the linking object.</param>
        /// <param name="linkingSourceFields">List of strings representing the linking source field names.</param>
        /// <param name="linkingTargetFields">List of strings representing the linking target field names.</param>
        private static Dictionary<string, Entity> GetSampleEntityMap(
            string sourceEntity,
            string targetEntity,
            string[] sourceFields,
            string[] targetFields,
            string linkingObject,
            string[] linkingSourceFields,
            string[] linkingTargetFields
        )
        {
            Dictionary<string, EntityRelationship> relationshipMap = new();

            // Creating relationship between source and target entity.
            EntityRelationship sampleRelationship = new(
                Cardinality: Cardinality.One,
                TargetEntity: targetEntity,
                SourceFields: sourceFields,
                TargetFields: targetFields,
                LinkingObject: linkingObject,
                LinkingSourceFields: linkingSourceFields,
                LinkingTargetFields: linkingTargetFields
            );

            relationshipMap.Add("rname1", sampleRelationship);

            Entity sampleEntity1 = GetSampleEntityUsingSourceAndRelationshipMap(
                source: "TEST_SOURCE1",
                relationshipMap: relationshipMap,
                graphQLDetails: new("rname1", "rname1s", true)
            );

            sampleRelationship = new(
                Cardinality: Cardinality.One,
                TargetEntity: sourceEntity,
                SourceFields: targetFields,
                TargetFields: sourceFields,
                LinkingObject: linkingObject,
                LinkingSourceFields: linkingTargetFields,
                LinkingTargetFields: linkingSourceFields
            );

            relationshipMap = new()
            {
                { "rname2", sampleRelationship }
            };

            Entity sampleEntity2 = GetSampleEntityUsingSourceAndRelationshipMap(
                source: "TEST_SOURCE2",
                relationshipMap: relationshipMap,
                graphQLDetails: new("rname2", "rname2s", true)
            );

            Dictionary<string, Entity> entityMap = new()
            {
                { sourceEntity, sampleEntity1 },
                { targetEntity, sampleEntity2 }
            };

            return entityMap;
        }

        /// <summary>
        /// Tests whether the API path prefix is well formed or not.
        /// </summary>
        /// <param name="apiPathPrefix">API path prefix</param>
        /// <param name="expectedErrorMessage">Expected error message in case an exception is thrown.</param>
        /// <param name="expectError">Exception expected</param>
        [DataTestMethod]
        [DataRow("/.", $"REST path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.REST, true,
            DisplayName = "REST path prefix containing reserved character .")]
        [DataRow("/:", $"REST path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.REST, true,
            DisplayName = "REST path prefix containing reserved character :")]
        [DataRow("/?", $"REST path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.REST, true,
            DisplayName = "REST path prefix containing reserved character ?")]
        [DataRow("/#", $"REST path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.REST, true,
            DisplayName = "REST path prefix containing reserved character #")]
        [DataRow("//", $"REST path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.REST, true,
            DisplayName = "REST path prefix containing reserved character /")]
        [DataRow("/[", $"REST path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.REST, true,
            DisplayName = "REST path prefix containing reserved character [")]
        [DataRow("/)", $"REST path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.REST, true,
            DisplayName = "REST path prefix containing reserved character )")]
        [DataRow("/@", $"REST path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.REST, true,
            DisplayName = "REST path prefix containing reserved character @")]
        [DataRow("/!", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved character !")]
        [DataRow("/$", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved character $")]
        [DataRow("/&", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved character &")]
        [DataRow("/'", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved character '")]
        [DataRow("/+", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved character +")]
        [DataRow("/;", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved character ;")]
        [DataRow("/=", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved character =")]
        [DataRow("/?#*(=", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing multiple reserved characters /?#*(=")]
        [DataRow("/+&,", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved characters /+&,")]
        [DataRow("/@)", $"GraphQL path {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing reserved characters /@)")]
        [DataRow("", "GraphQL path cannot be null or empty.", ApiType.GraphQL, true,
            DisplayName = "Empty GraphQL path prefix.")]
        [DataRow(null, "GraphQL path cannot be null or empty.", ApiType.GraphQL, true,
            DisplayName = "Null GraphQL path prefix.")]
        [DataRow("?", "GraphQL path should start with a '/'.", ApiType.GraphQL, true,
            DisplayName = "GraphQL path not starting with forward slash.")]
        [DataRow("/-api", null, ApiType.GraphQL, false,
            DisplayName = "GraphQL path containing hyphen (-)")]
        [DataRow("/api path", "GraphQL path contains white spaces.", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing space in between")]
        [DataRow("/ apipath", "REST path contains white spaces.", ApiType.REST, true,
            DisplayName = "REST path containing space at the start")]
        [DataRow("/ api_path", "GraphQL path contains white spaces.", ApiType.GraphQL, true,
            DisplayName = "GraphQL path prefix containing space at the start and underscore in between.")]
        [DataRow("/", null, ApiType.REST, false,
            DisplayName = "REST path containing only a forward slash.")]
        public void ValidateApiURIsAreWellFormed(
            string apiPathPrefix,
            string expectedErrorMessage,
            ApiType apiType,
            bool expectError)
        {
            string graphQLPathPrefix = GraphQLRuntimeOptions.DEFAULT_PATH;
            string restPathPrefix = RestRuntimeOptions.DEFAULT_PATH;
            string mcpPathPrefix = McpRuntimeOptions.DEFAULT_PATH;

            if (apiType is ApiType.REST)
            {
                restPathPrefix = apiPathPrefix;
            }
            else if (apiType is ApiType.MCP)
            {
                mcpPathPrefix = apiPathPrefix;
            }
            else
            {
                graphQLPathPrefix = apiPathPrefix;
            }

            GraphQLRuntimeOptions graphQL = new(Path: graphQLPathPrefix);
            RestRuntimeOptions rest = new(Path: restPathPrefix);
            McpRuntimeOptions mcp = new(Enabled: false);

            RuntimeConfig configuration = ConfigurationTests.InitMinimalRuntimeConfig(
                new(DatabaseType.MSSQL, "", Options: null),
                graphQL,
                rest,
                mcp);

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            if (expectError)
            {
                DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() =>
                configValidator.ValidateGlobalEndpointRouteConfig(configuration));
                Assert.AreEqual(expectedErrorMessage, ex.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
            else
            {
                configValidator.ValidateGlobalEndpointRouteConfig(configuration);
            }
        }

        /// <summary>
        /// Tests whether conflicting global REST/GraphQL fail config validation.
        /// </summary>
        /// <param name="restEnabled">Boolean flag to indicate if REST endpoints are enabled globally.</param>
        /// <param name="graphqlEnabled">Boolean flag to indicate if GraphQL endpoints are enabled globally.</param>
        /// <param name="mcpEnabled">Boolean flag to indicate if MCP endpoints are enabled globally.</param>
        /// <param name="expectError">Boolean flag to indicate if exception is expected.</param>
        [DataRow(true, true, true, false, DisplayName = "REST, GraphQL, and MCP enabled.")]
        [DataRow(true, true, false, false, DisplayName = "REST and GraphQL enabled, MCP disabled.")]
        [DataRow(true, false, true, false, DisplayName = "REST enabled, GraphQL disabled, and MCP enabled.")]
        [DataRow(true, false, false, false, DisplayName = "REST enabled, GraphQL and MCP disabled.")]
        [DataRow(false, true, true, false, DisplayName = "REST disabled, GraphQL and MCP enabled.")]
        [DataRow(false, true, false, false, DisplayName = "REST disabled, GraphQL enabled, and MCP disabled.")]
        [DataRow(false, false, true, false, DisplayName = "REST and GraphQL disabled, MCP enabled.")]
        [DataRow(false, false, false, true, DisplayName = "REST, GraphQL, and MCP disabled.")]
        [DataTestMethod]
        public void EnsureFailureWhenRestAndGraphQLAndMcpAreDisabled(
            bool restEnabled,
            bool graphqlEnabled,
            bool mcpEnabled,
            bool expectError)
        {
            GraphQLRuntimeOptions graphQL = new(Enabled: graphqlEnabled);
            RestRuntimeOptions rest = new(Enabled: restEnabled);
            McpRuntimeOptions mcp = new(Enabled: mcpEnabled);

            RuntimeConfig configuration = ConfigurationTests.InitMinimalRuntimeConfig(
                new(DatabaseType.MSSQL, "", Options: null),
                graphQL,
                rest,
                mcp);
            string expectedErrorMessage = "GraphQL, REST, and MCP endpoints are disabled.";

            try
            {
                RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();
                configValidator.ValidateGlobalEndpointRouteConfig(configuration);

                if (expectError)
                {
                    Assert.Fail(message: "Global endpoint route config validation expected to fail.");
                }
            }
            catch (DataApiBuilderException ex)
            {
                if (expectError)
                {
                    Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: ex.StatusCode);
                    Assert.AreEqual(expected: expectedErrorMessage, actual: ex.Message);
                }
                else
                {
                    Assert.Fail(message: "Global endpoint route config validation not expected to fail.");
                }
            }
        }

        /// <summary>
        /// Method to validate that the validations for include/exclude fields for an entity
        /// work as expected.
        /// </summary>
        /// <param name="databasePolicy">Database policy for a particular role/action combination for an entity.</param>
        /// <param name="isFieldsPresent">Boolean variable representing whether fields section is present in config.</param>
        /// <param name="includedFields">Fields that are accessible to user for the role/action combination.</param>
        /// <param name="excludedFields">Fields that are inaccessible to user for the role/action combination.</param>
        /// <param name="exceptionExpected">Whether an exception is expected (true when validation fails).</param>
        [DataTestMethod]
        [DataRow(@"""@item.id ne 140""", true, "[]", @"[ ""name"" ]", true,
            DisplayName = "Empty array for included fields and db policy referencing some field.")]
        [DataRow(@"""""", true, "[]", @"[ ""name"" ]", false,
            DisplayName = "Empty array for included fields and empty db policy.")]
        [DataRow(@"""@item.id ne @claims.userId and @item.name eq @claims.userDetails""", true, @"[ ""id"", ""name"" ]", @"[ ""title"" ]", false,
            DisplayName = "All fields referenced by db policy present in included.")]
        [DataRow(@"""@item.id ne @claims.userId and @item.name eq @claims.userDetails""", true, @"[ ""id"" ]", @"[""name""]", true,
            DisplayName = "One field referenced by db policy present in excluded.")]
        [DataRow(@"""@item.id ne @claims.userId and @item.name eq @claims.userDetails""", true, @"[]", @"[]", true,
            DisplayName = "Empty arrays for included/excluded fields and non-empty database policy.")]
        [DataRow(@"""@item.id ne @claims.userId and @item.name eq @claims.userDetails""", true, null, null, false,
            DisplayName = "NULL included/excluded fields and non-empty database policy.")]
        [DataRow(@"""@item.id ne @claims.userId and @item.name eq @claims.userDetails""", true, null,
            @"[ ""id"", ""name"" ]", true,
            DisplayName = "NULL included fields and fields referenced in database policy are excluded.")]
        [DataRow(@"""@item.id ne @claims.userId and @item.name eq @claims.userDetails""", true, null,
            @"[""title""]", false,
            DisplayName = "NULL included fields and fields referenced in database policy are not excluded.")]
        [DataRow(@"""@item.id ne @claims.userId and @item.name eq @claims.userDetails""", true, @"[ ""*"" ]",
            null, false, DisplayName = "NULL excluded fields and fields referenced in database policy are included via wildcard")]
        [DataRow(@"""@item.id ne @claims.userId and @item.name eq @claims.userDetails""", false, null,
            null, false,
            DisplayName = "fields section absent.")]
        public void TestFieldInclusionExclusion(
            string databasePolicy,
            bool isFieldsPresent,
            string includedFields,
            string excludedFields,
            bool exceptionExpected)
        {

            string fields = string.Empty;

            if (isFieldsPresent)
            {
                string prefix = @",""fields"": {";
                string includeFields = includedFields is null ? string.Empty : @"""include"" : " + includedFields;
                string joinIncludeExclude = includedFields is not null && excludedFields is not null ? "," : string.Empty;
                string excludeFields = excludedFields is null ? string.Empty : @"""exclude"" : " + excludedFields;
                string postfix = "}";
                fields = prefix + includeFields + joinIncludeExclude + excludeFields + postfix;
            }

            string runtimeConfigString = @"{
                    " +
                @"""$schema"": ""test_schema""," +
                @"""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": """ + SAMPLE_TEST_CONN_STRING + @""",
                    ""options"":{
                        ""set-session-context"": false
                    }
                },
                ""runtime"": {
                    ""host"": {
                    ""mode"": ""development"",
                    ""authentication"": {
                        ""provider"": ""StaticWebApps""
                    }
                  }
                },
                ""entities"": {
                    ""Publisher"":{
                        ""source"": ""publishers"",
                        ""permissions"": [
                           {
                            ""role"": ""anonymous"",
                            ""actions"": [
                               {
                                ""action"": ""Read"",
                                ""policy"": {
                                    ""database"":" + databasePolicy +
                                  @"}" + fields +
                               @"}
                            ]
                           }
                         ]
                        }
                    }
                }";

            RuntimeConfigLoader.TryParseConfig(runtimeConfigString, out RuntimeConfig runtimeConfig);
            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            // Perform validation on the permissions in the config and assert the expected results.
            if (exceptionExpected)
            {
                DataApiBuilderException ex =
                    Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
                Assert.AreEqual("Not all the columns required by policy are accessible.", ex.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
            else
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
        }

        /// <summary>
        /// Method to validate that any field set (included/excluded) if misconfigured, throws an exception during
        /// config validation stage. If any field set contains a wildcard and any other field, we consider it as misconfigured.
        /// </summary>
        /// <param name="databasePolicy">Database policy for a particular role/action combination for an entity.</param>
        /// <param name="includedFields">Fields that are accessible to user for the role/action combination.</param>
        /// <param name="excludedFields">Fields that are inaccessible to user for the role/action combination.</param>
        /// <param name="exceptionExpected">Whether an exception is expected (true when validation fails).</param>
        /// <param name="misconfiguredColumnSet">Name of the misconfigured column set (included/excluded).</param>
        [DataTestMethod]
        [DataRow(@"""@item.id ne 140""", @"[ ""*"", ""id"" ]", @"[ ""name"" ]", true, "included",
            DisplayName = "Included fields containing wildcard and another field.")]
        [DataRow(@"""@item.id ne 140""", @"[ ""*"", ""id"" ]", @"[ ""*"", ""name"" ]", true, "excluded",
            DisplayName = "Excluded fields containing wildcard and another field.")]
        [DataRow(@"""@item.id ne 140""", null, @"[ ""*"", ""name"" ]", true, "excluded",
            DisplayName = "Excluded fields containing wildcard and another field and included fields is null.")]
        [DataRow(@"""@item.id ne 140""", @"[ ""*"", ""name"" ]", null, true, "included",
            DisplayName = "Included fields containing wildcard and another field and excluded fields is null.")]
        [DataRow(@"""@item.id ne 140""", @"[ ""*"" ]", @"[ ""name"" ]", false, null,
            DisplayName = "Well configured include/exclude fields.")]
        public void ValidateMisconfiguredColumnSets(
            string databasePolicy,
            string includedFields,
            string excludedFields,
            bool exceptionExpected,
            string misconfiguredColumnSet)
        {

            string fields = string.Empty;

            string prefix = @",""fields"": {";
            string includeFields = includedFields is null ? string.Empty : @"""include"" : " + includedFields;
            string joinIncludeExclude = includedFields is not null && excludedFields is not null ? "," : string.Empty;
            string excludeFields = excludedFields is null ? string.Empty : @"""exclude"" : " + excludedFields;
            string postfix = "}";
            fields = prefix + includeFields + joinIncludeExclude + excludeFields + postfix;

            string runtimeConfigString = @"{
                    " +
                @"""$schema"": ""test_schema""," +
                @"""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": """ + SAMPLE_TEST_CONN_STRING + @""",
                    ""options"":{
                        ""set-session-context"": false
                    }
                },
                ""runtime"": {
                    ""host"": {
                    ""mode"": ""development"",
                    ""authentication"": {
                        ""provider"": ""StaticWebApps""
                    }
                  }
                },
                ""entities"": {
                    ""Publisher"":{
                        ""source"": ""publishers"",
                        ""permissions"": [
                           {
                            ""role"": ""anonymous"",
                            ""actions"": [
                               {
                                ""action"": ""Read"",
                                ""policy"": {
                                    ""database"":" + databasePolicy +
                                  @"}" + fields +
                               @"}
                            ]
                           }
                         ]
                        }
                    }
                }";

            RuntimeConfigLoader.TryParseConfig(runtimeConfigString, out RuntimeConfig runtimeConfig);
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            RuntimeConfigValidator configValidator = new(provider, fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);

            // Perform validation on the permissions in the config and assert the expected results.
            if (exceptionExpected)
            {
                DataApiBuilderException ex =
                    Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
                Assert.AreEqual($"No other field can be present with wildcard in the {misconfiguredColumnSet} " +
                    $"set for: entity:Publisher, role:anonymous, action:Read", ex.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
            else
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
        }

        /// <summary>
        /// Validates that a warning is logged when REST methods are configured for tables and views. For stored procedures,
        /// no warnings are logged.
        /// </summary>
        /// <param name="sourceType">The source type of the entity.</param>
        /// <param name="methods">Value of the rest methods property configured for the entity.</param>
        /// <param name="exceptionExpected">Boolean value representing whether an exception is expected or not.</param>
        /// <param name="expectedErrorMessage">Expected error message when an exception is expected for the test run.</param>
        [DataTestMethod]
        [DataRow(EntitySourceType.Table, new SupportedHttpVerb[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post }, true,
            DisplayName = "Tables with REST Methods configured - Engine logs a warning during startup")]
        [DataRow(EntitySourceType.StoredProcedure, new SupportedHttpVerb[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post }, false,
            DisplayName = "Stored Procedures with REST Methods configured - No warnings logged")]
        public void ValidateRestMethodsForEntityInConfig(
            EntitySourceType sourceType,
            SupportedHttpVerb[] methods,
            bool isWarningLogExpected)
        {
            Dictionary<string, Entity> entityMap = new();
            string entityName = "EntityA";
            // Sets REST method for the entity
            Entity entity = new(Source: new("TEST_SOURCE", sourceType, null, null),
                                 Rest: new(Methods: methods),
                                 GraphQL: new(entityName, ""),
                                 Permissions: Array.Empty<EntityPermission>(),
                                 Relationships: new(),
                                 Mappings: new());
            entityMap.Add(entityName, entity);

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, string.Empty, Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)),
                Entities: new(entityMap));

            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            Mock<ILogger<RuntimeConfigValidator>> loggerMock = new();
            RuntimeConfigValidator configValidator = new(provider, fileSystem, loggerMock.Object);

            configValidator.ValidateEntityConfiguration(runtimeConfig);

            if (isWarningLogExpected)
            {
                // Assert on the log message to verify the warning log
                loggerMock.Verify(
                    x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Equals($"Entity {entityName} has rest methods configured but is not a stored procedure. Values configured will be ignored and all 5 HTTP actions will be enabled.")),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()
                    ),
                    Times.Once
                );
            }
        }

        /// <summary>
        /// Test to validate that the rest path for an entity cannot be empty and cannot contain any reserved characters.
        /// </summary>
        /// <param name="exceptionExpected">Whether an exception is expected as a result of test run.</param>
        /// <param name="restPathForEntity">Custom rest path to be configured for the first entity.</param>
        /// <param name="expectedExceptionMessage">The expected exception message.</param>
        [DataTestMethod]
        [DataRow(true, "EntityA", "", true, "The rest path for entity: EntityA cannot be empty.",
            DisplayName = "Empty rest path configured for an entity fails config validation.")]
        [DataRow(true, "EntityA", "entity?RestPath", true, "The rest path: entity?RestPath for entity: EntityA contains one or more reserved characters.",
            DisplayName = "Rest path for an entity containing reserved character ? fails config validation.")]
        [DataRow(true, "EntityA", "entity#RestPath", true, "The rest path: entity#RestPath for entity: EntityA contains one or more reserved characters.",
            DisplayName = "Rest path for an entity containing reserved character ? fails config validation.")]
        [DataRow(true, "EntityA", "entity[]RestPath", true, "The rest path: entity[]RestPath for entity: EntityA contains one or more reserved characters.",
            DisplayName = "Rest path for an entity containing reserved character ? fails config validation.")]
        [DataRow(true, "EntityA", "entity+Rest*Path", true, "The rest path: entity+Rest*Path for entity: EntityA contains one or more reserved characters.",
            DisplayName = "Rest path for an entity containing reserved character ? fails config validation.")]
        [DataRow(true, "Entity?A", null, true, "The rest path: Entity?A for entity: Entity?A contains one or more reserved characters.",
            DisplayName = "Entity name for an entity containing reserved character ? fails config validation.")]
        [DataRow(true, "Entity&*[]A", null, true, "The rest path: Entity&*[]A for entity: Entity&*[]A contains one or more reserved characters.",
            DisplayName = "Entity name containing reserved character ? fails config validation.")]
        [DataRow(false, "EntityA", "entityRestPath", true, DisplayName = "Rest path correctly configured as a non-empty string without any reserved characters.")]
        [DataRow(false, "EntityA", "entityRest/?Path", false,
            DisplayName = "Rest path for an entity containing reserved character but with rest disabled passes config validation.")]
        public void ValidateRestPathForEntityInConfig(
            bool exceptionExpected,
            string entityName,
            string restPathForEntity,
            bool isRestEnabledForEntity,
            string expectedExceptionMessage = "")
        {
            Dictionary<string, Entity> entityMap = new();
            Entity sampleEntity = GetSampleEntityUsingSourceAndRelationshipMap(
                source: "TEST_SOURCEA",
                relationshipMap: null,
                graphQLDetails: null,
                restDetails: new(new SupportedHttpVerb[] { }, restPathForEntity, isRestEnabledForEntity)
            );
            entityMap.Add(entityName, sampleEntity);

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap)
            );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            if (exceptionExpected)
            {
                DataApiBuilderException dabException =
                    Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidateEntityConfiguration(runtimeConfig));
                Assert.AreEqual(expectedExceptionMessage, dabException.Message);
                Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: dabException.StatusCode);
                Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: dabException.SubStatusCode);
            }
            else
            {
                configValidator.ValidateEntityConfiguration(runtimeConfig);
            }
        }

        /// <summary>
        /// Test to validate that when multiple entities have the same rest path configured, we throw an exception.
        /// </summary>
        /// <param name="exceptionExpected">Whether an exception is expected as a result of test run.</param>
        /// <param name="restPathForFirstEntity">Custom rest path to be configured for the first entity.</param>
        /// <param name="restPathForSecondEntity">Custom rest path to be configured for the second entity.</param>
        /// <param name="expectedExceptionMessage">The expected exception message.</param>
        [DataTestMethod]
        [DataRow(false, "restPathA", "restPathB", true, true, DisplayName = "Unique rest paths configured for entities pass config validation.")]
        [DataRow(true, "restPath", "restPath", true, true, "The rest path: restPath specified for entity: EntityB is already used by another entity.",
            DisplayName = "Duplicate rest paths configured for entities fail config validation.")]
        [DataRow(false, "restPath", "restPath", true, false,
            DisplayName = "Duplicate rest paths configured for entities with rest disabled on one of them pass config validation.")]
        [DataRow(false, "restPath", "restPath", false, false,
            DisplayName = "Duplicate rest paths configured for entities with rest disabled on both of them pass config validation.")]
        [DataRow(true, null, "EntityA", true, true, "The rest path: EntityA specified for entity: EntityB is already used by another entity.",
            DisplayName = "Rest path for an entity configured as the name of another entity fails config validation.")]
        public void ValidateUniqueRestPathsForEntitiesInConfig(
            bool exceptionExpected,
            string restPathForFirstEntity,
            string restPathForSecondEntity,
            bool isRestEnabledForFirstEntity,
            bool isRestEnabledForSecondEntity,
            string expectedExceptionMessage = "")
        {
            Dictionary<string, Entity> entityMap = new();
            Entity sampleEntityA = GetSampleEntityUsingSourceAndRelationshipMap(
                source: "TEST_SOURCEA",
                relationshipMap: null,
                graphQLDetails: null,
                restDetails: new(new SupportedHttpVerb[] { }, restPathForFirstEntity, isRestEnabledForFirstEntity)
            );

            Entity sampleEntityB = GetSampleEntityUsingSourceAndRelationshipMap(
                source: "TEST_SOURCEB",
                relationshipMap: null,
                graphQLDetails: null,
                restDetails: new(new SupportedHttpVerb[] { }, restPathForSecondEntity, isRestEnabledForSecondEntity)
            );

            entityMap.Add("EntityA", sampleEntityA);
            entityMap.Add("EntityB", sampleEntityB);

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap)
            );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            if (exceptionExpected)
            {
                DataApiBuilderException dabException =
                    Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidateEntityConfiguration(runtimeConfig));
                Assert.AreEqual(expectedExceptionMessage, dabException.Message);
                Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: dabException.StatusCode);
                Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: dabException.SubStatusCode);
            }
            else
            {
                configValidator.ValidateEntityConfiguration(runtimeConfig);
            }
        }

        /// <summary>
        /// Validates that the runtime base-route is well-formatted and does not contain any reserved characeters and
        /// can only be configured when authentication provider is Static Web Apps.
        /// </summary>
        /// <param name="runtimeBaseRoute">Value of runtime base-route as specified in config.</param>
        /// <param name="authenticationProvider">The authentication provider configured.</param>
        /// <param name="isExceptionExpected">Whether an exception is expected as a result of test run.</param>
        /// <param name="expectedExceptionMessage">The expected exception message.</param>
        [DataTestMethod]
        [DataRow("/base-route", "StaticWebApps", false, DisplayName = "Runtime base-route correctly configured as '/base-route' for Static Web Apps.")]
        [DataRow("/", "StaticWebApps", false, DisplayName = "Runtime base-route correctly configured as '/' for Static Web Apps.")]
        [DataRow(null, "AppService", false,
            DisplayName = "Runtime base-route specified as null for AppService authentication provider passes config validation.")]
        [DataRow(null, "StaticWebApps", false,
            DisplayName = "Runtime base-route specified as null for Static Web Apps authentication provider passes config validation.")]
        [DataRow("/    ", "StaticWebApps", true, "Runtime base-route contains white spaces.",
            DisplayName = "Runtime base-route specified as whitespace string for Static Web Apps authentication provider passes config validation.")]
        [DataRow("/    ", "AppService", true, "Runtime base-route can only be used when the authentication provider is Static Web Apps.",
            DisplayName = "Runtime base-route specified as whitespace string for AppService authentication provider fails config validation.")]
        [DataRow("/base-route", "AppService", true, "Runtime base-route can only be used when the authentication provider is Static Web Apps.",
            DisplayName = "Runtime base-route specified for non-Static Web Apps authentication provider - AppService.")]
        [DataRow("/base+?route", "StaticWebApps", true, $"Runtime base-route {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}",
            DisplayName = "Runtime base-route specified for Static Web Apps authentication provider containing reserved characters +?")]
        [DataRow("/base%&#route", "StaticWebApps", true, $"Runtime base-route {RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG}",
            DisplayName = "Runtime base-route specified for Static Web Apps authentication provider containing reserved characters %&#")]
        [DataRow("base-route", "StaticWebApps", true, $"Runtime base-route should start with a '/'.",
            DisplayName = "Runtime base-route specified for Static Web Apps authentication provider not starting with '/'")]
        public void ValidateRuntimeBaseRouteSettings(
            string runtimeBaseRoute,
            string authenticationProvider,
            bool isExceptionExpected,
            string expectedExceptionMessage = "")
        {
            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(Cors: null, Authentication: new(Provider: authenticationProvider, Jwt: null)),
                    BaseRoute: runtimeBaseRoute
                ),
                Entities: new(new Dictionary<string, Entity>())
            );

            RuntimeConfigValidator configValidator = InitializeRuntimeConfigValidator();

            if (!isExceptionExpected)
            {
                configValidator.ValidateGlobalEndpointRouteConfig(runtimeConfig);
            }
            else
            {
                DataApiBuilderException dabException =
                    Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidateGlobalEndpointRouteConfig(runtimeConfig));
                Assert.AreEqual(expectedExceptionMessage, dabException.Message);
                Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: dabException.StatusCode);
                Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: dabException.SubStatusCode);
            }
        }

        /// <summary>
        /// This test checks that the final config used by runtime engine doesn't lose the directory information
        /// if provided by the user.
        /// It also validates that if config file is provided by the user, it will be used directly irrespective of
        /// environment variable being set or not. 
        /// When user doesn't provide a config file, we check if environment variable is set and if it is, we use
        /// the config file specified by the environment variable, else we use the default config file.
        /// <param name="userProvidedConfigFilePath"></param>
        /// <param name="environmentValue"></param>
        /// <param name="useAbsolutePath"></param>
        /// <param name="environmentFile"></param>
        /// <param name="finalConfigFilePath"></param>
        [DataTestMethod]
        [DataRow("my-config.json", "", false, null, "my-config.json", DisplayName = "Config file in the current directory provided by user and environment variable is not set")]
        [DataRow("test-configs/my-config.json", "", false, null, "test-configs/my-config.json", DisplayName = "Config file in different directory provided by user and environment variable is not set")]
        [DataRow("my-config.json", "Test", false, "my-config.Test.json", "my-config.json", DisplayName = "Config file in the current directory provided by user and environment variable is set")]
        [DataRow("test-configs/my-config.json", "Test", false, "test-configs/my-config.Test.json", "test-configs/my-config.json", DisplayName = "Config file in different directory provided by user and environment variable is set")]
        [DataRow("my-config.json", "Test", false, "dab-config.Test.json", "my-config.json", DisplayName = "Config file in the current directory provided by user and environment variable is set and environment file is present")]
        [DataRow("test-configs/my-config.json", "Test", false, "test-configs/dab-config.Test.json", "test-configs/my-config.json", DisplayName = "Config file in different directory provided by user and environment variable is set and environment file is present")]
        [DataRow("my-config.json", "", true, null, "my-config.json", DisplayName = "Config file in the current directory provided by user and environment variable is not set and absolute path is provided")]
        [DataRow("test-configs/my-config.json", "", true, null, "test-configs/my-config.json", DisplayName = "Config file in different directory provided by user and environment variable is not set and absolute path is provided")]
        [DataRow("my-config.json", "Test", true, "my-config.Test.json", "my-config.json", DisplayName = "Config file in the current directory provided by user and environment variable is set and absolute path is provided")]
        [DataRow("test-configs/my-config.json", "Test", true, "test-configs/my-config.Test.json", "test-configs/my-config.json", DisplayName = "Config file in different directory provided by user and environment variable is set and absolute path is provided")]
        [DataRow("my-config.json", "Test", true, "dab-config.Test.json", "my-config.json", DisplayName = "Config file in the current directory provided by user and environment variable is set and environment file is present and absolute path is provided")]
        [DataRow("test-configs/my-config.json", "Test", true, "test-configs/dab-config.Test.json", "test-configs/my-config.json", DisplayName = "Config file in the different directory provided by user and environment variable is set and environment file is present and absolute path is provided")]
        [DataRow(null, "", false, null, "dab-config.json", DisplayName = "Config file not provided by user and environment variable is not set")]
        [DataRow(null, "Test", false, "dab-config.Test.json", "dab-config.json", DisplayName = "Config file not provided by user and environment variable is set and environment file is present")]
        [DataRow(null, "Test", false, null, "dab-config.json", DisplayName = "Config file not provided by user and environment variable is set and environment file is not present")]
        public void TestCorrectConfigFileIsSelectedForRuntimeEngine(
            string userProvidedConfigFilePath,
            string environmentValue,
            bool useAbsolutePath,
            string environmentFile,
            string finalConfigFilePath)
        {
            MockFileSystem fileSystem = new();
            if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(userProvidedConfigFilePath)))
            {
                fileSystem.AddDirectory("test-configs");
            }

            if (useAbsolutePath)
            {
                userProvidedConfigFilePath = fileSystem.Path.GetFullPath(userProvidedConfigFilePath);
                finalConfigFilePath = fileSystem.Path.GetFullPath(finalConfigFilePath);
            }

            if (environmentFile is not null)
            {
                fileSystem.AddEmptyFile(environmentFile);
            }

            FileSystemRuntimeConfigLoader runtimeConfigLoader;
            if (userProvidedConfigFilePath is not null)
            {
                runtimeConfigLoader = new(fileSystem, handler: null, userProvidedConfigFilePath);
            }
            else
            {
                runtimeConfigLoader = new(fileSystem);
            }

            Assert.AreEqual(finalConfigFilePath, runtimeConfigLoader.ConfigFilePath);
        }

        /// <summary>
        /// Method to validate that runtimeConfig is successfully set up using constructor
        /// where members are passed in without json config file.
        /// RuntimeConfig has two constructors, one that loads from the config json and one that takes in all the members.
        /// This test makes sure that the constructor that takes in all the members works as expected.
        /// </summary>
        [TestMethod]
        public void TestRuntimeConfigSetupWithNonJsonConstructor()
        {
            EntitySource entitySource = new(
                    Type: EntitySourceType.Table,
                    Object: "sourceName",
                    Parameters: null,
                    KeyFields: null
                );

            Entity sampleEntity1 = new(
                Source: entitySource,
                GraphQL: null,
                Rest: null,
                Permissions: null,
                Mappings: null,
                Relationships: null);

            string entityName = "SampleEntity1";
            string dataSourceName = "Test1";

            Dictionary<string, Entity> entityMap = new()
            {
                { entityName, sampleEntity1 }
            };

            DataSource testDataSource = new(DatabaseType: DatabaseType.MSSQL, "", Options: null);
            Dictionary<string, DataSource> dataSourceNameToDataSource = new()
            {
                { dataSourceName, testDataSource }
            };

            Dictionary<string, string> entityNameToDataSourceName = new()
            {
                { entityName, dataSourceName }
            };

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: testDataSource,
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null)
                ),
                Entities: new RuntimeEntities(entityMap),
                DefaultDataSourceName: dataSourceName,
                DataSourceNameToDataSource: dataSourceNameToDataSource,
                EntityNameToDataSourceName: entityNameToDataSourceName
            );

            Assert.AreEqual(testDataSource, runtimeConfig.DataSource, "RuntimeConfig datasource must match datasource passed into constructor");
            Assert.AreEqual(dataSourceNameToDataSource.Count(), runtimeConfig.ListAllDataSources().Count(),
                "RuntimeConfig datasource count must match datasource count passed into constructor");
            Assert.IsTrue(runtimeConfig.SqlDataSourceUsed,
                $"Config has a sql datasource and member {nameof(runtimeConfig.SqlDataSourceUsed)} must be marked as true.");
            Assert.IsFalse(runtimeConfig.CosmosDataSourceUsed,
                $"Config does not have a cosmos datasource and member {nameof(runtimeConfig.CosmosDataSourceUsed)} must be marked as false.");
        }

        /// <summary>
        /// Test to validate pagination options.
        /// NOTE: Changing the default values of default page size and max page size would be a breaking change.
        /// </summary>
        /// <param name="exceptionExpected">Should there be an exception.</param>
        /// <param name="defaultPageSize">default page size to go into config.</param>
        /// <param name="maxPageSize">max page size to go into config.</param>
        /// <param name="expectedExceptionMessage">expected exception message in case there is exception.</param>
        /// <param name="expectedDefaultPageSize">expected default page size from config.</param>
        /// <param name="expectedMaxPageSize">expected max page size from config.</param>
        [DataTestMethod]
        [DataRow(false, null, null, "", (int)PaginationOptions.DEFAULT_PAGE_SIZE, (int)PaginationOptions.MAX_PAGE_SIZE,
            DisplayName = "MaxPageSize should be 100,000 and DefaultPageSize should be 100 when no value provided in config.")]
        [DataRow(false, 1000, 10000, "", 1000, 10000,
            DisplayName = "Valid inputs of MaxPageSize and DefaultPageSize must be accepted and set in the config.")]
        [DataRow(false, -1, 10000, "", 10000, 10000,
            DisplayName = "DefaultPageSize should be the same as MaxPageSize when DefaultPageSize is -1 in config.")]
        [DataRow(false, 100, -1, "", 100, Int32.MaxValue,
            DisplayName = "MaxPageSize should be assigned UInt32.MaxValue when MaxPageSize is -1 in config.")]
        [DataRow(true, 100, -3, "Pagination options invalid. Page size arguments cannot be 0, exceed max int value or be less than -1",
            DisplayName = "MaxPageSize cannot be negative")]
        [DataRow(true, -3, 100, "Pagination options invalid. Page size arguments cannot be 0, exceed max int value or be less than -1",
            DisplayName = "DefaultPageSize cannot be negative")]
        [DataRow(true, 100, 0, "Pagination options invalid. Page size arguments cannot be 0, exceed max int value or be less than -1",
            DisplayName = "MaxPageSize cannot be 0")]
        [DataRow(true, 0, 100, "Pagination options invalid. Page size arguments cannot be 0, exceed max int value or be less than -1",
            DisplayName = "DefaultPageSize cannot be 0")]
        [DataRow(true, 101, 100, "Pagination options invalid. The default page size cannot be greater than max page size",
            DisplayName = "DefaultPageSize cannot be greater than MaxPageSize")]
        [DataRow(false, null, null, "", (int)PaginationOptions.DEFAULT_PAGE_SIZE, (int)PaginationOptions.MAX_PAGE_SIZE, null,
            DisplayName = "NextLinkRelative should be false when no value provided in config")]
        [DataRow(false, null, null, "", (int)PaginationOptions.DEFAULT_PAGE_SIZE, (int)PaginationOptions.MAX_PAGE_SIZE, true,
            DisplayName = "NextLinkRelative should be true when explicitly set to true in config")]
        [DataRow(false, null, null, "", (int)PaginationOptions.DEFAULT_PAGE_SIZE, (int)PaginationOptions.MAX_PAGE_SIZE, false,
            DisplayName = "NextLinkRelative should be false when explicitly set to false in config")]
        [DataRow(false, 1000, 10000, "", 1000, 10000, true,
            DisplayName = "NextLinkRelative with custom page sizes")]
        public void ValidatePaginationOptionsInConfig(
            bool exceptionExpected,
            int? defaultPageSize,
            int? maxPageSize,
            string expectedExceptionMessage,
            int? expectedDefaultPageSize = null,
            int? expectedMaxPageSize = null,
            bool? nextLinkRelative = null)
        {
            try
            {
                RuntimeConfig runtimeConfig = new(
                    Schema: "UnitTestSchema",
                    DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                    Runtime: new(
                        Rest: new(),
                        GraphQL: new(),
                        Mcp: new(),
                        Host: new(Cors: null, Authentication: null),
                        Pagination: new PaginationOptions(defaultPageSize, maxPageSize, nextLinkRelative)
                    ),
                    Entities: new(new Dictionary<string, Entity>()));

                Assert.AreEqual((uint)expectedDefaultPageSize, runtimeConfig.DefaultPageSize());
                Assert.AreEqual((uint)expectedMaxPageSize, runtimeConfig.MaxPageSize());
                Assert.AreEqual(expected: nextLinkRelative ?? false, actual: runtimeConfig.NextLinkRelative());
            }
            catch (DataApiBuilderException dabException)
            {
                Assert.IsTrue(exceptionExpected);
                Assert.AreEqual(expectedExceptionMessage, dabException.Message);
                Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: dabException.StatusCode);
                Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: dabException.SubStatusCode);
            }
        }

        /// <summary>
        /// Test to validate the max response size option in the runtime config.
        /// Note:Changing the default values of max response size would be a breaking change.
        /// </summary>
        /// <param name="exceptionExpected">should there be an exception.</param>
        /// <param name="maxDbResponseSizeMB">maxResponse size input</param>
        /// <param name="expectedExceptionMessage">expected exception message in case there is exception.</param>
        /// <param name="expectedMaxResponseSize">expected value in config.</param>
        [DataTestMethod]
        [DataRow(null, 158, false, "",
            DisplayName = $"{nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)} should be 158MB when no value provided in config.")]
        [DataRow(64, 64, false, "",
            DisplayName = $"Valid positive input of {nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)}  > 0 and <= 158MB must be accepted and set in the config.")]
        [DataRow(-1, 158, false, "",
            DisplayName = $"-1 user input for {nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)} should result in a value of 158MB which is the max value supported by dab engine")]
        [DataRow(0, null, true, $"{nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)} cannot be 0, exceed 158MB or be less than -1",
            DisplayName = $"Input of 0 for {nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)} must throw exception.")]
        [DataRow(159, null, true, $"{nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)} cannot be 0, exceed 158MB or be less than -1",
            DisplayName = $"Inputs of {nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)} greater than 158MB must throw exception.")]
        public void ValidateMaxResponseSizeInConfig(
            int? providedMaxResponseSizeMB,
            int? expectedMaxResponseSizeMB,
            bool isExceptionExpected,
            string expectedExceptionMessage)
        {
            try
            {
                RuntimeConfig runtimeConfig = new(
                    Schema: "UnitTestSchema",
                    DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                    Runtime: new(
                        Rest: new(),
                        GraphQL: new(),
                        Mcp: new(),
                        Host: new(Cors: null, Authentication: null, MaxResponseSizeMB: providedMaxResponseSizeMB)
                    ),
                    Entities: new(new Dictionary<string, Entity>()));
                Assert.AreEqual(expectedMaxResponseSizeMB, runtimeConfig.MaxResponseSizeMB());
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(isExceptionExpected);
                Assert.AreEqual(expectedExceptionMessage, ex.Message);
            }
        }

        private static RuntimeConfigValidator InitializeRuntimeConfigValidator()
        {
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            return new(provider, fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);
        }
    }
}

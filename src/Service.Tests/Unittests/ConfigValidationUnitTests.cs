using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.Authorization;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Test class to perform semantic validations on the runtime config object. At this point,
    /// the tests focus on the permissions portion of the entities property within the runtimeconfig object.
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
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                includedCols: new HashSet<string> { "*" },
                excludedCols: new HashSet<string> { "id", "email" },
                databasePolicy: dbPolicy
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
            Assert.AreEqual("Not all the columns required by policy are accessible.", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test method to validate that an appropriate exception is thrown when there is an invalid action
        /// supplied in the runtimeconfig.
        /// </summary>
        /// <param name="dbPolicy">Database policy.</param>
        /// <param name="action">The action to be validated.</param>
        [DataTestMethod]
        [DataRow("@claims.id eq @item.col1", Operation.Insert, DisplayName = "Invalid action Insert specified in config")]
        [DataRow("@claims.id eq @item.col2", Operation.Upsert, DisplayName = "Invalid action Upsert specified in config")]
        [DataRow("@claims.id eq @item.col3", Operation.UpsertIncremental, DisplayName = "Invalid action UpsertIncremental specified in config")]
        public void InvalidActionSpecifiedForARole(string dbPolicy, Operation action)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                action,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: dbPolicy
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
            Assert.AreEqual($"action:{action.ToString()} specified for entity:{AuthorizationHelpers.TEST_ENTITY}," +
                    $" role:{AuthorizationHelpers.TEST_ROLE} is not valid.", ex.Message);
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
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: dbPolicy
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
            Assert.AreEqual("Claimtype cannot be empty.", ex.Message);
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
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: policy
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
            Assert.IsTrue(ex.Message.StartsWith("Invalid format for claim type"));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test to validate that wildcard action passes all stages of config validation.
        /// </summary>
        [TestMethod]
        public void WildcardActionSpecifiedForARole()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.All,
                includedCols: new HashSet<string> { "col1", "col2", "col3" }
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);

            // All the validations would pass, and no exception would be thrown.
            configValidator.ValidatePermissionsInConfig(runtimeConfig);
        }

        /// <summary>
        /// Test to validate that no other field can be present in included set if wildcard is present
        /// in it.
        /// </summary>
        [DataTestMethod]
        [DataRow(Operation.Create, DisplayName = "Wildcard Field with another field in included set and create action")]
        [DataRow(Operation.Update, DisplayName = "Wildcard Field with another field in included set and update action")]
        public void WildCardAndOtherFieldsPresentInIncludeSet(Operation actionOp)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                actionOp,
                includedCols: new HashSet<string> { "*", "col2" }
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
            string actionName = actionOp.ToString();
            Assert.AreEqual($"No other field can be present with wildcard in the included set for: entity:{AuthorizationHelpers.TEST_ENTITY}," +
                $" role:{AuthorizationHelpers.TEST_ROLE}, action:{actionName}", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [DataTestMethod]
        [DataRow(Operation.Create, DisplayName = "Wildcard Field with another field in excluded set and create action")]
        [DataRow(Operation.Update, DisplayName = "Wildcard Field with another field in excluded set and update action")]
        public void WildCardAndOtherFieldsPresentInExcludeSet(Operation actionOp)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                actionOp,
                excludedCols: new HashSet<string> { "*", "col1" }
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);

            // Assert that expected exception is thrown.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => configValidator.ValidatePermissionsInConfig(runtimeConfig));
            string actionName = actionOp.ToString();
            Assert.AreEqual($"No other field can be present with wildcard in the excluded set for: entity:{AuthorizationHelpers.TEST_ENTITY}," +
                $" role:{AuthorizationHelpers.TEST_ROLE}, action:{actionName}", ex.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        /// <summary>
        /// Test that entity names from config that are invalid GraphQL names
        /// fail runtime config validation.
        /// Asserts that an exception is thrown, and with that exception object,
        /// validates the exception's status and substatus codes.
        /// </summary>
        /// <param name="entityNameFromConfig"></param>
        [DataTestMethod]
        [DataRow("@entityname", DisplayName = "Invalid start character @")]
        [DataRow("_entityname", DisplayName = "Invalid start character _")]
        [DataRow("#entityname", DisplayName = "Invalid start character #")]
        [DataRow("5Entityname", DisplayName = "Invalid start character 5")]
        [DataRow("E.ntityName", DisplayName = "Invalid body character .")]
        [DataRow("Entity^Name", DisplayName = "Invalid body character ^")]
        [DataRow("Entity&Name", DisplayName = "Invalid body character &")]
        [DataRow("Entity name", DisplayName = "Invalid body character whitespace")]

        public void InvalidGraphQLTypeNamesFailValidation(string entityNameFromConfig)
        {
            Dictionary<string, Entity> entityCollection = new();
            entityCollection.Add(entityNameFromConfig, SchemaConverterTests.GenerateEmptyEntity());

            DataApiBuilderException dabException = Assert.ThrowsException<DataApiBuilderException>(
                action: () => RuntimeConfigValidator.ValidateEntityNamesInConfig(entityCollection),
                message: $"Entity name \"{entityNameFromConfig}\" incorrectly passed validation.");

            Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: dabException.StatusCode);
            Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: dabException.SubStatusCode);
        }

        /// <summary>
        /// Test that entity names from config that are valid GraphQL names
        /// pass runtime config validation. This test method fails if any
        /// exceptions are thrown.
        /// </summary>
        /// <param name="entityNameFromConfig"></param>
        [DataTestMethod]
        [DataRow("entityname", DisplayName = "Valid lower case letter as first character")]
        [DataRow("Entityname", DisplayName = "Valid upper case letter as first character")]
        [DataRow("Entity_name", DisplayName = "Valid _ in body")]
        public void ValidGraphQLTypeNamesPassValidation(string entityNameFromConfig)
        {
            Dictionary<string, Entity> entityCollection = new();
            entityCollection.Add(entityNameFromConfig, SchemaConverterTests.GenerateEmptyEntity());

            RuntimeConfigValidator.ValidateEntityNamesInConfig(entityCollection);
        }

        [DataTestMethod]
        [DataRow("book", "books", false, DisplayName = "Valid GraphQL Settings - Singular and Plural specified")]
        [DataRow("book", null, false, DisplayName = "Valid GraphQL Settings - Singular specified")]
        [DataRow(null, null, true, DisplayName = "Invalid GraphQL Settings - Both not specified")]
        [DataRow(null, "books", true, DisplayName = "Invalid GraphQL Settings - Singular not specified")]
        [DataRow(" ", "books", true, DisplayName = "Invalid GraphQL Settings - Singular just contains whitespaces")]
        [DataRow("", "books", true, DisplayName = "Invalid GraphQL Settings - Singular is an empty string")]
        public void ValidateSingularPluralGraphQLSettingsInEntity(
                string singularName,
                string pluralName,
                bool isExceptionExpected)
        {
            Dictionary<string, Entity> entityCollection = new();
            entityCollection.Add("book", GraphQLTestHelpers.GenerateEntityWithSingularPlural(singularName, pluralName));
            if (isExceptionExpected)
            {
                DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                    () => RuntimeConfigValidator.ValidateGraphQLSettingsForEntitiesInConfig(entityCollection));
                Assert.AreEqual($"Entity book has an invalid singular name for GraphQL", ex.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
            else
            {
                try
                {
                    RuntimeConfigValidator.ValidateGraphQLSettingsForEntitiesInConfig(entityCollection);

                }
                catch (System.Exception ex)
                {
                    Assert.Fail("Exception thrown for a valid SingularPlural GraphQL type");
                }

            }

        }

        [DataTestMethod]
        [DataRow("book", false, DisplayName = "Valid Singular Name")]
        [DataRow("@book", true, DisplayName = "InValid Singular Name - Starts with @")]
        [DataRow("_book", true, DisplayName = "Invalid Singular Name - Starts with _")]
        [DataRow("bo&ok", true, DisplayName = "Invalid Singular Name - Contains &")]
        [DataRow("b!ook", true, DisplayName = "Invalid Singular Name - Contains !")]
        [DataRow("b ook", true, DisplayName = "Invalid Singular Name - Contains space")]
        public void ValidateSingularNameInGraphQLEntitySettings(string singularName, bool isExceptionExpected)
        {
            Dictionary<string, Entity> entityCollection = new();
            entityCollection.Add("book", GraphQLTestHelpers.GenerateEntityWithSingularPlural(singularName, null));
            if (isExceptionExpected)
            {
                DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                    () => RuntimeConfigValidator.ValidateGraphQLSettingsForEntitiesInConfig(entityCollection));
                Assert.AreEqual($"book's singular definition contains characters disallowed by GraphQL.", ex.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
            else
            {
                try
                {
                    RuntimeConfigValidator.ValidateGraphQLSettingsForEntitiesInConfig(entityCollection);

                }
                catch (System.Exception ex)
                {
                    Assert.Fail("Exception thrown for a valid SingularPlural GraphQL type");
                }

            }

        }

        [DataTestMethod]
        [DataRow("book", false, DisplayName = "Valid Plural Name")]
        [DataRow("@book", true, DisplayName = "InValid Plural Name - Starts with @")]
        [DataRow("_book", true, DisplayName = "Invalid Plural Name - Starts with _")]
        [DataRow("bo&ok", true, DisplayName = "Invalid Plural Name - Contains &")]
        [DataRow("b!ook", true, DisplayName = "Invalid Plural Name - Contains !")]
        [DataRow("b ook", true, DisplayName = "Invalid Plural Name - Contains space")]
        public void ValidatePluralNameInGraphQLEntitySettings(string pluralName, bool isExceptionExpected)
        {
            Dictionary<string, Entity> entityCollection = new();
            entityCollection.Add("book", GraphQLTestHelpers.GenerateEntityWithSingularPlural("book", pluralName));
            if (isExceptionExpected)
            {
                DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                    () => RuntimeConfigValidator.ValidateGraphQLSettingsForEntitiesInConfig(entityCollection));
                Assert.AreEqual($"book's plural definition contains characters disallowed by GraphQL.", ex.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
            else
            {
                try
                {
                    RuntimeConfigValidator.ValidateGraphQLSettingsForEntitiesInConfig(entityCollection);

                }
                catch (System.Exception ex)
                {
                    Assert.Fail("Exception thrown for a valid SingularPlural GraphQL type");
                }

            }

        }

        [DataTestMethod]
        [DataRow("books", false, DisplayName = "Valid GraphQL Type ")]
        [DataRow("", true, DisplayName = "Invalid GraphQL Type - Empty String")]
        [DataRow("  ", true, DisplayName = "Invalid GraphQL Type - Just Whitespaces")]
        [DataRow("#book", true, DisplayName = "Invalid GraphQL Type - Starts with #")]
        [DataRow("5book", true, DisplayName = "Invalid GraphQL Type - Starts with a number")]
        [DataRow("book!", true, DisplayName = "Invalid GraphQL Type - Contains a !")]
        [DataRow("bo ok", true, DisplayName = "Invalid GraphQL Type - Contains a space")]
        public void ValidateStringGraphQLSettingsInEntity(string type, bool isExceptionExpected)
        {
            Dictionary<string, Entity> entityCollection = new();
            entityCollection.Add("book", GraphQLTestHelpers.GenerateEntityWithStringType(type));
            if (isExceptionExpected)
            {
                DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                    () => RuntimeConfigValidator.ValidateGraphQLSettingsForEntitiesInConfig(entityCollection));
                Assert.AreEqual($"Entity book has an invalid string for GraphQL", ex.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
            else
            {
                try
                {
                    RuntimeConfigValidator.ValidateGraphQLSettingsForEntitiesInConfig(entityCollection);
                }
                catch (System.Exception ex)
                {
                    Assert.Fail("Exception thrown for a valid string GraphQL type");
                }

            }

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
        [DataRow("remove", true, DisplayName = "Invalid operation name remove specified for action")]
        [DataRow("inseRt", true, DisplayName = "Invalid operation name inseRt specified for action")]
        public void TestOperationValidityAndCasing(string operationName, bool exceptionExpected)
        {
            string actionJson = @"{
                                        ""action"": " + $"\"{operationName}\"" + @",
                                        ""policy"": {
                                            ""database"": ""@claims.id eq @item.id""
                                          },
                                        ""fields"": {
                                            ""include"": [""*""]
                                          }
                                  }";
            object actionForRole = JsonSerializer.Deserialize<object>(actionJson);

            PermissionSetting permissionForEntity = new(
                role: AuthorizationHelpers.TEST_ROLE,
                actions: new object[] { JsonSerializer.SerializeToElement(actionForRole) });

            Entity sampleEntity = new(
                Source: AuthorizationHelpers.TEST_ENTITY,
                Rest: null,
                GraphQL: null,
                Permissions: new PermissionSetting[] { permissionForEntity },
                Relationships: null,
                Mappings: null
                );

            Dictionary<string, Entity> entityMap = new();
            entityMap.Add(AuthorizationHelpers.TEST_ENTITY, sampleEntity);

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                MsSql: null,
                CosmosDb: null,
                PostgreSql: null,
                MySql: null,
                DataSource: new DataSource(DatabaseType: DatabaseType.mssql),
                RuntimeSettings: new Dictionary<GlobalSettingsType, object>(),
                Entities: entityMap
                );

            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);
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
                Assert.AreEqual($"action:{operationName} specified for entity:{AuthorizationHelpers.TEST_ENTITY}," +
                    $" role:{AuthorizationHelpers.TEST_ROLE} is not valid.",
                    ex.Message);
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
        public void ValidateGraphQLTypeNamesFromConfig(string entityNameFromConfig, bool expectsException)
        {
            Dictionary<string, Entity> entityCollection = new();

            // Sets only the top level name and enables GraphQL for entity
            Entity entity = SchemaConverterTests.GenerateEmptyEntity();
            entity.GraphQL = true;
            entityCollection.Add(entityNameFromConfig, entity);

            // Sets the top level name to an arbitrary value since it is not used in this check
            // and enables GraphQL for entity by setting the GraphQLSettings.Type to a string.
            entity = SchemaConverterTests.GenerateEmptyEntity();
            entity.GraphQL = new GraphQLEntitySettings(Type: entityNameFromConfig);
            entityCollection.Add("EntityA", entity);

            // Sets the top level name to an arbitrary value since it is not used in this check
            // and enables GraphQL for entity by setting the GraphQLSettings.Type to
            // a SingularPlural object where only Singular is defined.
            entity = SchemaConverterTests.GenerateEmptyEntity();
            SingularPlural singularPlural = new(Singular: entityNameFromConfig, Plural: null);
            entity.GraphQL = new GraphQLEntitySettings(Type: singularPlural);
            entityCollection.Add("EntityB", entity);

            // Sets the top level name to an arbitrary value since it is not used in this check
            // and enables GraphQL for entity by setting the GraphQLSettings.Type to
            // a SingularPlural object where both Singular and Plural are defined.
            entity = SchemaConverterTests.GenerateEmptyEntity();
            singularPlural = new(Singular: entityNameFromConfig, Plural: entityNameFromConfig);
            entity.GraphQL = new GraphQLEntitySettings(Type: singularPlural);
            entityCollection.Add("EntityC", entity);

            if (expectsException)
            {
                DataApiBuilderException dabException = Assert.ThrowsException<DataApiBuilderException>(
                    action: () => RuntimeConfigValidator.ValidateEntityNamesInConfig(entityCollection),
                    message: $"Entity name \"{entityNameFromConfig}\" incorrectly passed validation.");

                Assert.AreEqual(expected: HttpStatusCode.ServiceUnavailable, actual: dabException.StatusCode);
                Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.ConfigValidationError, actual: dabException.SubStatusCode);
            }
            else
            {
                RuntimeConfigValidator.ValidateEntityNamesInConfig(entityCollection);
            }
        }
    }
}

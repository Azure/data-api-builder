using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.Authorization;
using Azure.DataApiBuilder.Service.Tests.Configuration;
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

using System.Collections.Generic;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Tests.Authorization;
using Azure.DataGateway.Service.Tests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.UnitTests
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
            try
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
            catch (DataGatewayException ex)
            {
                Assert.AreEqual("Not all the columns required by policy are accessible.", ex.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
            }
        }

        /// <summary>
        /// Test method to validate that an appropriate exception is thrown when there is an invalid action
        /// supplied in the runtimeconfig.
        /// </summary>
        /// <param name="dbPolicy">Database policy.</param>
        /// <param name="action">The action to be validated.</param>
        [DataTestMethod]
        [DataRow("@claims.id eq @item.col1", Operation.Insert, DisplayName = "Invalid action insert specified in config")]
        [DataRow("@claims.id eq @item.col2", Operation.Upsert, DisplayName = "Invalid action upsert specified in config")]
        [DataRow("@claims.id eq @item.col3", Operation.Find, DisplayName = "Invalid action find specified in config")]
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
            try
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
            catch (DataGatewayException ex)
            {
                Assert.AreEqual($"action:{action.ToString()} specified for entity:{AuthorizationHelpers.TEST_ENTITY}," +
                    $" role:{AuthorizationHelpers.TEST_ROLE} is not valid.", ex.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
            }
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
            try
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
            catch (DataGatewayException ex)
            {
                Assert.AreEqual("Claimtype cannot be empty.", ex.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
            }
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
            try
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
            catch (DataGatewayException ex)
            {
                Assert.IsTrue(ex.Message.StartsWith("Invalid format for claim type"));
                Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
            }
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
            try
            {
                // All the validations would pass.
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
            catch
            {
                // This block should not be hit.
                Assert.Fail();
            }
        }

        /// <summary>
        /// Test to validate that no other field can be present in included set if wildcard is present
        /// in it.
        /// </summary>
        [DataTestMethod]
        [DataRow(Operation.All, DisplayName = "Wildcard Field with another field in included set test1")]
        [DataRow(Operation.Update, DisplayName = "Wildcard Field with another field in included set test2")]
        public void WildCardAndOtherFieldsPresentInIncludeSet(Operation actionOp)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                actionOp,
                includedCols: new HashSet<string> { "*", "col2" }
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);
            try
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
            catch (DataGatewayException ex)
            {
                string actionName = actionOp is Operation.All ? "*" : actionOp.ToString();
                Assert.AreEqual($"No other field can be present with wildcard in the included set for: entity:{AuthorizationHelpers.TEST_ENTITY}," +
                    $" role:{AuthorizationHelpers.TEST_ROLE}, action:{actionName}", ex.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
                Assert.AreEqual(DataGatewayException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
        }

        [DataTestMethod]
        [DataRow(Operation.All, DisplayName = "Wildcard Field with another field in included set test1")]
        [DataRow(Operation.Update, DisplayName = "Wildcard Field with another field in included set test2")]
        public void WildCardAndOtherFieldsPresentInExcludeSet(Operation actionOp)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                actionOp,
                excludedCols: new HashSet<string> { "*", "col1" }
                );
            RuntimeConfigValidator configValidator = AuthenticationConfigValidatorUnitTests.GetMockConfigValidator(ref runtimeConfig);
            try
            {
                configValidator.ValidatePermissionsInConfig(runtimeConfig);
            }
            catch (DataGatewayException ex)
            {
                string actionName = actionOp is Operation.All ? "*" : actionOp.ToString();
                Assert.AreEqual($"No other field can be present with wildcard in the excluded set for: entity:{AuthorizationHelpers.TEST_ENTITY}," +
                    $" role:{AuthorizationHelpers.TEST_ROLE}, action:{actionName}", ex.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
                Assert.AreEqual(DataGatewayException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
            }
        }
    }
}

using System.Collections.Generic;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.Authorization
{
    [TestClass]
    public class AuthorizationResolverUnitTests
    {
        #region Role Context Tests
        /// <summary>
        /// When the client role header is present, validates result when
        /// Role is in ClaimsPrincipal.Roles -> VALID
        /// Role is NOT in ClaimsPrincipal.Roles -> INVALID
        /// </summary>
        [DataTestMethod]
        [DataRow("Reader", true, true)]
        [DataRow("Reader", false, false)]
        public void ValidRoleContext_Simple(string clientRoleHeaderValue, bool userIsInRole, bool expected)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig();
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(clientRoleHeaderValue);
            context.Setup(x => x.User.IsInRole(clientRoleHeaderValue)).Returns(userIsInRole);

            Assert.AreEqual(authZResolver.IsValidRoleContext(context.Object), expected);
        }

        [TestMethod("Role header has no value")]
        public void RoleHeaderEmpty()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig();
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(StringValues.Empty);
            bool expected = false;

            Assert.AreEqual(authZResolver.IsValidRoleContext(context.Object), expected);
        }

        [TestMethod("Role header has multiple values")]
        public void RoleHeaderDuplicated()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig();
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            StringValues multipleValuesForHeader = new(new string[] { "Reader", "Writer" });
            context.SetupGet(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(multipleValuesForHeader);
            context.Setup(x => x.User.IsInRole("Reader")).Returns(true);
            bool expected = false;

            Assert.AreEqual(authZResolver.IsValidRoleContext(context.Object), expected);
        }

        [TestMethod("Role header is missing")]
        public void NoRoleHeader_RoleContextTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig();
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(StringValues.Empty);
            bool expected = false;

            Assert.AreEqual(authZResolver.IsValidRoleContext(context.Object), expected);
        }
        #endregion
        #region Role and Action on Entity Tests
        /// <summary>
        /// Tests the AreRoleAndActionDefinedForEntity stage of authorization.
        /// Request Action is defined for role -> VALID
        /// Request Action not defined for role (role has 0 defined actions)
        ///     Ensures method short ciruits in circumstances role is not defined -> INVALID
        /// Request Action does not match an action defined for role (role has >=1 defined action) -> INVALID
        /// </summary>
        [DataTestMethod]
        [DataRow("Writer", ActionType.CREATE, "Writer", ActionType.CREATE, true)]
        [DataRow("Reader", ActionType.CREATE, "Reader", "", false)]
        [DataRow("Writer", ActionType.CREATE, "Writer", ActionType.UPDATE, false)]
        public void AreRoleAndActionDefinedForEntityTest(
            string configRole,
            string configAction,
            string roleName,
            string actionName,
            bool expected)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(AuthorizationHelpers.TEST_ENTITY, configRole, configAction);
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values
            Assert.AreEqual(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, roleName, actionName), expected);
        }
        #endregion

        /// <summary>
        /// Tests the authorization stage: Columns defined for Action
        /// Columns are allowed for role
        /// Columns are not allowed for role
        /// Wildcard included and/or excluded columns handling
        /// and assumes request validation has already occurred
        /// </summary>
        #region Positive Column Tests
        [TestMethod("Column allowed for action on role")]
        public void ColsDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed.
            List<string> columns = new(new string[] { "col1" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("All Columns allowed for action on role")]
        public void ColDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            List<string> columns = new(new string[] { "col1", "col2", "col3" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Wildcard included columns allowed for action on role")]
        public void WildcardIncludeColDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            List<string> columns = new(new string[] { "col1", "col2", "col3" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Wildcard excluded columns with some included for action on role success")]
        public void WildcardIncludeColsSomeExcludeDefinedForActionSuccess_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "*" },
                excludedCols: new string[] { "col1", "col2" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed.
            List<string> columns = new(new string[] { "col3", "col4" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }
        #endregion
        #region Negative Column Tests
        [TestMethod("Columns NOT allowed for action on role")]
        public void ColsNotDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column NOT allowed.
            List<string> columns = new(new string[] { "col4" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Columns NOT allowed for action on role - with some valid cols")]
        public void ColsNotDefinedForAction2_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action
            // to match all allowed columns, with one NOT allowed column.
            List<string> columns = new(new string[] { "col1", "col2", "col3", "col4" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Columns NOT allowed for action on role - definition has inc/excl - req has only excluded cols")]
        public void ColsNotDefinedForAction3_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "col1", "col2", "col3" },
                excludedCols: new string[] { "col4", "col5", "col6" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with multiple columns NOT allowed.
            List<string> columns = new(new string[] { "col4", "col5" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Columns NOT allowed for action on role - Mixed allowed/disallowed in req.")]
        public void ColsNotDefinedForAction4Mixed_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "col1", "col2", "col3" },
                excludedCols: new string[] { "col4", "col5", "col6" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with 1 allowed/ 1 disallwed column(s).
            List<string> columns = new(new string[] { "col2", "col5" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Wildcard excluded for action on role")]
        public void WildcardExcludeColsDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns not allowed.
            List<string> columns = new(new string[] { "col1", "col2", "col3" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Wildcard include all except some columns for action on role")]
        public void WildcardIncludeColsSomeExcludeDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "*" },
                excludedCols: new string[] { "col1", "col2" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed and column not allowed.
            List<string> columns = new(new string[] { "col3", "col1" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Wildcard exclude all except for some columns for action on role - Request with excluded column")]
        public void WildcardExcludeColsSomeIncludeDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "col1", "col2" },
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with two columns allowed, one not.
            List<string> columns = new(new string[] { "col1", "col2", "col3" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        [TestMethod("Wildcard exclude all except some columns for action on role - Request with all included columns")]
        public void WildcardExcludeColsSomeIncludeDefinedForActionSuccess_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new string[] { "col1", "col2" },
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            List<string> columns = new(new string[] { "col1", "col2" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }
        #endregion
        #region Helpers
        #endregion
    }
}

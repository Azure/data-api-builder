using System;
using System.Collections.Generic;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Action = Azure.DataGateway.Config.Action;

namespace Azure.DataGateway.Service.Tests.Authorization
{
    [TestClass]
    public class AuthorizationResolverUnitTests
    {
        private const string TEST_ENTITY = "SampleEntity";
        private const string TEST_ROLE = "Writer";
        private const string TEST_ACTION = "Create";

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
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(clientRoleHeaderValue);
            context.Setup(x => x.User.IsInRole(clientRoleHeaderValue)).Returns(userIsInRole);

            Assert.AreEqual(authZResolver.IsValidRoleContext(context.Object), expected);
        }

        [TestMethod("Role header has no value")]
        public void RoleHeaderEmpty()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(StringValues.Empty);
            bool expected = false;

            Assert.AreEqual(authZResolver.IsValidRoleContext(context.Object), expected);
        }

        [TestMethod("Role header has multiple values")]
        public void RoleHeaderDuplicated()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

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
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

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
        [DataRow("Writer", "Create", "Writer", "Create", true)]
        [DataRow("Reader", "Create", "Reader", "", false)]
        [DataRow("Writer", "Create", "Writer", "Update", false)]
        public void AreRoleAndActionDefinedForEntityTest(
            string configRole,
            string configAction,
            string roleName,
            string actionName,
            bool expected)
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(TEST_ENTITY, configRole, configAction);
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values
            Assert.AreEqual(authZResolver.AreRoleAndActionDefinedForEntity(TEST_ENTITY, roleName, actionName), expected);
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
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed.
            List<string> columns = new(new string[] { "col1" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("All Columns allowed for action on role")]
        public void ColDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            List<string> columns = new(new string[] { "col1", "col2", "col3" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Wildcard included columns allowed for action on role")]
        public void WildcardIncludeColDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            List<string> columns = new(new string[] { "col1", "col2", "col3" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Wildcard excluded columns with some included for action on role success")]
        public void WildcardIncludeColsSomeExcludeDefinedForActionSuccess_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "*" },
                excludedCols: new string[] { "col1", "col2" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed.
            List<string> columns = new(new string[] { "col3", "col4" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }
        #endregion
        #region Negative Column Tests
        [TestMethod("Columns NOT allowed for action on role")]
        public void ColsNotDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column NOT allowed.
            List<string> columns = new(new string[] { "col4" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Columns NOT allowed for action on role - with some valid cols")]
        public void ColsNotDefinedForAction2_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action
            // to match all allowed columns, with one NOT allowed column.
            List<string> columns = new(new string[] { "col1", "col2", "col3", "col4" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Columns NOT allowed for action on role - definition has inc/excl - req has only excluded cols")]
        public void ColsNotDefinedForAction3_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "col1", "col2", "col3" },
                excludedCols: new string[] { "col4", "col5", "col6" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with multiple columns NOT allowed.
            List<string> columns = new(new string[] { "col4", "col5" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Columns NOT allowed for action on role - Mixed allowed/disallowed in req.")]
        public void ColsNotDefinedForAction4Mixed_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "col1", "col2", "col3" },
                excludedCols: new string[] { "col4", "col5", "col6" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with 1 allowed/ 1 disallwed column(s).
            List<string> columns = new(new string[] { "col2", "col5" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Wildcard excluded for action on role")]
        public void WildcardExcludeColsDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns not allowed.
            List<string> columns = new(new string[] { "col1", "col2", "col3" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Wildcard include all except some columns for action on role")]
        public void WildcardIncludeColsSomeExcludeDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "*" },
                excludedCols: new string[] { "col1", "col2" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed and column not allowed.
            List<string> columns = new(new string[] { "col3", "col1" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Wildcard exclude all except for some columns for action on role - Request with excluded column")]
        public void WildcardExcludeColsSomeIncludeDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "col1", "col2" },
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with two columns allowed, one not.
            List<string> columns = new(new string[] { "col1", "col2", "col3" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }

        [TestMethod("Wildcard exclude all except some columns for action on role - Request with all included columns")]
        public void WildcardExcludeColsSomeIncludeDefinedForActionSuccess_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new string[] { "col1", "col2" },
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            List<string> columns = new(new string[] { "col1", "col2" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(TEST_ENTITY, TEST_ROLE, TEST_ACTION, columns), expected);
        }
        #endregion
        #region Helpers
        private static AuthorizationResolver InitAuthZResolver(RuntimeConfig runtimeConfig)
        {
            Mock<IOptionsMonitor<RuntimeConfig>> runtimeConfigProvider = new();
            runtimeConfigProvider.Setup(x => x.CurrentValue).Returns(runtimeConfig);

            return new AuthorizationResolver(runtimeConfigProvider.Object.CurrentValue);
        }
        private static RuntimeConfig InitRuntimeConfig(
            string entityName = "SampleEntity",
            string roleName = "Reader",
            string actionName = "Create",
            string[] includedCols = null,
            string[] excludedCols = null
            )
        {
            Field fieldsForRole = new(
                Include: includedCols,
                Exclude: excludedCols);

            Action actionForRole = new(
                Name: actionName,
                Fields: fieldsForRole,
                Policy: null);

            PermissionSetting permissionForEntity = new(
                Role: roleName,
                Actions: new object[] { JsonSerializer.SerializeToElement(actionForRole) });

            Entity sampleEntity = new(
                Source: new String("SQL"),
                Rest: null,
                GraphQL: null,
                Permissions: new PermissionSetting[] { permissionForEntity },
                Relationships: null,
                Mappings: null
                );

            Dictionary<string, Entity> entityMap = new();
            entityMap.Add(entityName, sampleEntity);

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

            return runtimeConfig;
        }
        #endregion
    }
}

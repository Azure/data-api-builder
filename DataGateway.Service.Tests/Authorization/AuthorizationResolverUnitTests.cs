using System;
using System.Collections.Generic;
using Azure.DataGateway.Config;
using Action = Azure.DataGateway.Config.Action;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.Authorization
{
    [TestClass]
    public class AuthorizationResolverUnitTests
    {
        /// <summary>
        /// Tests the first stage of authorization: Role Context
        /// X-MS-API-ROLE header is present, Role is in ClaimsPrincipal.Roles
        /// X-MS-API-ROLE header is present, Role is NOT in ClaimsPrincipal.Roles
        /// X-MS-API-ROLE header is present, value is empty
        /// X-MS-API-ROLE header is present, and header is duplicated(fuzzing catch)
        /// X-MS-API-ROLE header is NOT present
        /// </summary>
        #region Positive Role Context Tests
        [TestMethod("X-MS-API-ROLE header is present, Role is in ClaimsPrincipal.Roles")]
        public void ValidRole_ContextTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers["X-MS-API-ROLE"]).Returns("Reader");
            context.Setup(x => x.Request.Headers.ContainsKey("X-MS-API-ROLE")).Returns(true);
            context.Setup(x => x.User.IsInRole("Reader")).Returns(true);

            Assert.IsTrue(authZResolver.IsValidRoleContext(context.Object));
        }
        #endregion
        #region Negative Role Context Tests
        [TestMethod("X-MS-API-ROLE header is present, Role is NOT in ClaimsPrincipal.Roles")]
        public void UserNotInRole_RoleContextTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers["X-MS-API-ROLE"]).Returns("Reader");
            context.Setup(x => x.Request.Headers.ContainsKey("X-MS-API-ROLE")).Returns(true);
            context.Setup(x => x.User.IsInRole("Reader")).Returns(false);

            Assert.IsFalse(authZResolver.IsValidRoleContext(context.Object));
        }

        [TestMethod("X-MS-API-ROLE header is present, value is empty")]
        public void RoleHeaderEmpty_RoleContextTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers["X-MS-API-ROLE"]).Returns(string.Empty);
            context.Setup(x => x.Request.Headers.ContainsKey("X-MS-API-ROLE")).Returns(true);

            Assert.IsFalse(authZResolver.IsValidRoleContext(context.Object));
        }

        [TestMethod("X-MS-API-ROLE header is duplicated / has multiple values")]
        public void RoleHeaderDuplicated_RoleContextTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            StringValues multipleValuesForHeader = new(new string[] { "Reader", "Writer" });
            context.SetupGet(x => x.Request.Headers["X-MS-API-ROLE"]).Returns(multipleValuesForHeader);
            context.Setup(x => x.Request.Headers.ContainsKey("X-MS-API-ROLE")).Returns(true);
            context.Setup(x => x.User.IsInRole("Reader")).Returns(true);

            Assert.IsFalse(authZResolver.IsValidRoleContext(context.Object));
        }

        [TestMethod("X-MS-API-ROLE header is not present.")]
        public void NoRoleHeader_RoleContextTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig();
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            Mock<HttpContext> context = new();
            context.SetupGet(x => x.Request.Headers["X-MS-API-ROLE"]).Returns(StringValues.Empty);
            context.Setup(x => x.Request.Headers.ContainsKey("X-MS-API-ROLE")).Returns(true);

            Assert.IsFalse(authZResolver.IsValidRoleContext(context.Object));
        }
        #endregion
        /// <summary>
        /// Tests the second stage of authorization: Role defined for Entity.
        /// Role is defined for entity
        /// Role is not defined for entity
        /// </summary>
        #region Positve Role Tests
        [TestMethod("Role is defined for entity")]
        public void RoleDefinedForEntity_EntityHasRoleTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(entityName: "SampleEntity", roleName: "Writer");
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity with a role that is NOT configured.
            string entityName = "SampleEntity";
            string roleName = "Writer";

            Assert.IsTrue(authZResolver.IsRoleDefinedForEntity(roleName, entityName));
        }
        #endregion
        #region Negative Role Tests
        [TestMethod("Role is NOT defined for entity")]
        public void RoleNotDefinedForEntity_EntityHasRoleTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(entityName: "SampleEntity", roleName: "Writer");
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity with a role that is NOT configured.
            string entityName = "SampleEntity";
            string roleName = "Reader";

            Assert.IsFalse(authZResolver.IsRoleDefinedForEntity(roleName, entityName));
        }
        #endregion

        /// <summary>
        /// Tests the third stage of authorization: Action defined for Role on Entity
        /// Action is defined for role
        /// Action is not defined for role
        /// </summary>
        #region Positive Action Tests
        [TestMethod("Action is defined for role")]
        public void ActionDefinedForRole_RoleHasActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(entityName: "SampleEntity", roleName: "Writer", actionName: "Create");
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity and role with action that is configured.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";

            Assert.IsTrue(authZResolver.IsActionAllowedForRole(roleName, entityName, actionName));
        }
        #endregion
        #region Negative Action Tests
        [TestMethod("Action is NOT defined for role")]
        public void ActionNotDefinedForRole_RoleHasActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(entityName: "SampleEntity", roleName: "Writer", actionName: "Create");
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity and role with action that is NOT configured.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Update";

            Assert.IsFalse(authZResolver.IsActionAllowedForRole(roleName, entityName, actionName));
        }
        #endregion

        /// <summary>
        /// Tests the fourth stage of authorization: Columns defined for Action
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
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col1" });

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("All Columns allowed for action on role")]
        public void ColDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col1", "col2", "col3" });

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Wildcard included columns allowed for action on role")]
        public void WildcardIncludeColDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col1", "col2", "col3" });

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Wildcard excluded columns with some included for action on role")]
        public void WildcardExcludeColsSomeIncludeDefinedForActionSuccess_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "col1", "col2" },
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col1", "col2" });

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Wildcard excluded columns with some included for action on role success")]
        public void WildcardIncludeColsSomeExcludeDefinedForActionSuccess_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "*" },
                excludedCols: new string[] { "col1", "col2" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col3", "col4" });

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }
        #endregion
        #region Negative Column Tests
        [TestMethod("Columns NOT allowed for action on role")]
        public void ColsNotDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column NOT allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col4" });

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Columns NOT allowed for action on role - with some valid cols")]
        public void ColsNotDefinedForAction2_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "col1", "col2", "col3" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action
            // to match all allowed columns, with one NOT allowed column.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col1", "col2", "col3", "col4" });

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Columns NOT allowed for action on role - definition has inc/excl - req has only excluded cols")]
        public void ColsNotDefinedForAction3_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "col1", "col2", "col3" },
                excludedCols: new string[] { "col4", "col5", "col6" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with multiple columns NOT allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col4", "col5" });

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Columns NOT allowed for action on role - Mixed allowed/disallowed in req.")]
        public void ColsNotDefinedForAction4Mixed_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "col1", "col2", "col3" },
                excludedCols: new string[] { "col4", "col5", "col6" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with 1 allowed/ 1 disallwed column(s).
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col2", "col5" });

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Wildcard excluded for action on role")]
        public void WildcardExcludeColsDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns not allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col1", "col2", "col3" });

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Wildcard excluded except for some included for action on role")]
        public void WildcardExcludeColsSomeIncludeDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "col1", "col2" },
                excludedCols: new string[] { "*" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with two columns allowed, one not.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col1", "col2", "col3" });

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }

        [TestMethod("Wildcard excluded except for some included for action on role")]
        public void WildcardIncludeColsSomeExcludeDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                entityName: "SampleEntity",
                roleName: "Writer",
                actionName: "Create",
                includedCols: new string[] { "*" },
                excludedCols: new string[] { "col1", "col2" }
                );
            AuthorizationResolver authZResolver = InitAuthZResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed and column not allowed.
            string entityName = "SampleEntity";
            string roleName = "Writer";
            string actionName = "Create";
            List<string> columns = new(new string[] { "col3", "col1" });

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(roleName, entityName, actionName, columns));
        }
        #endregion
        #region Helpers
        private static AuthorizationResolver InitAuthZResolver(RuntimeConfig runtimeConfig)
        {
            Mock<IRuntimeConfigProvider> runtimeConfigProvider = new();
            runtimeConfigProvider.Setup(x => x.GetRuntimeConfig()).Returns(runtimeConfig);
            return new AuthorizationResolver(runtimeConfigProvider.Object);
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
                Actions: new object[] { actionForRole });

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
                DataSource: new DataSource(DatabaseType: DatabaseType.mssql, ConnectionString: "sample"),
                RuntimeSettings: new Dictionary<GlobalSettingsType, GlobalSettings>(),
                Entities: entityMap
                );

            return runtimeConfig;
        }
        #endregion
    }
}

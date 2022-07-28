#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Microsoft.AspNetCore.Http;
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
        private const string TEST_ACTION = ActionType.CREATE;
        private const string TEST_AUTHENTICATION_TYPE = "TestAuth";
        private const string TEST_CLAIMTYPE_NAME = "TestName";
        private const string TEST_ROLE_TYPE = "TestRole";

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
            context.Setup(x => x.User.Identity!.IsAuthenticated).Returns(true);

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
            Assert.AreEqual(expected, authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, roleName, actionName));
        }

        /// <summary>
        /// Test that wildcard actions are expanded to explicit actions.
        /// Verifies that internal data structure are created correctly.
        /// </summary>
        [TestMethod("Wildcard actions are expand to all valid actions")]
        public void TestWildcardAction()
        {
            List<string> expectedRoles = new() { AuthorizationHelpers.TEST_ROLE };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                AuthorizationResolver.WILDCARD);

            // Override the action to be a list of string for wildcard instead of a list of object created by InitRuntimeConfig()
            //
            runtimeConfig.Entities[AuthorizationHelpers.TEST_ENTITY].Permissions[0].Actions = new object[] { JsonSerializer.SerializeToElement(AuthorizationResolver.WILDCARD) };
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // There should not be a wildcard action in AuthorizationResolver.EntityPermissionsMap
            //
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, AuthorizationResolver.WILDCARD));

            // All the wildcard action should be expand to explicit actions.
            //
            foreach (string actionName in RuntimeConfigValidator.ValidActions)
            {
                Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, actionName));

                IEnumerable<string> actualRolesForCol1 = authZResolver.GetRolesForField(AuthorizationHelpers.TEST_ENTITY, "col1", actionName);

                CollectionAssert.AreEquivalent(expectedRoles, actualRolesForCol1.ToList());

                IEnumerable<string> actualRolesForAction = authZResolver.GetRolesForAction(AuthorizationHelpers.TEST_ENTITY, actionName);
                CollectionAssert.AreEquivalent(expectedRoles, actualRolesForAction.ToList());
            }

            // Validate that the authorization check fails because the actions are invalid.
            //
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, TEST_ROLE, "patch"));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, TEST_ROLE, "fetch"));
        }

        /// <summary>
        /// Verify that the internal data structure is created correctly when we have
        /// Two roles for the same entity with different permission.
        /// readOnlyRole - Read permission only for col1 and no policy.
        /// readAndUpdateRole - read and update permission for col1 and no policy.
        /// </summary>
        [TestMethod]
        public void TestRoleAndActionCombination()
        {
            const string READ_ONLY_ROLE = "readOnlyRole";
            const string READ_AND_UPDATE_ROLE = "readAndUpdateRole";

            Field fieldsForRole = new(
                include: new HashSet<string> { "col1" },
                exclude: null);

            Action readAction = new(
                Name: ActionType.READ,
                Fields: fieldsForRole,
                Policy: null);

            Action updateAction = new(
                Name: ActionType.UPDATE,
                Fields: fieldsForRole,
                Policy: null);

            PermissionSetting readOnlyPermission = new(
                role: READ_ONLY_ROLE,
                actions: new object[] { JsonSerializer.SerializeToElement(readAction) });

            PermissionSetting readAndUpdatePermission = new(
            role: READ_AND_UPDATE_ROLE,
            actions: new object[] { JsonSerializer.SerializeToElement(readAction), JsonSerializer.SerializeToElement(updateAction) });

            Entity sampleEntity = new(
                Source: TEST_ENTITY,
                Rest: null,
                GraphQL: null,
                Permissions: new PermissionSetting[] { readOnlyPermission, readAndUpdatePermission },
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

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Verify that read only role has permission for read and nothing else.
            //
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, READ_ONLY_ROLE, ActionType.READ));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, READ_ONLY_ROLE, ActionType.UPDATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, READ_ONLY_ROLE, ActionType.CREATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, READ_ONLY_ROLE, ActionType.DELETE));

            // Verify that read only role has permission for read and nothing else.
            //
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, READ_AND_UPDATE_ROLE, ActionType.READ));
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, READ_AND_UPDATE_ROLE, ActionType.UPDATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, READ_AND_UPDATE_ROLE, ActionType.CREATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, READ_AND_UPDATE_ROLE, ActionType.DELETE));

            List<string> expectedRolesForRead = new() { READ_ONLY_ROLE, READ_AND_UPDATE_ROLE };
            List<string> expectedRolesForUpdate = new() { READ_AND_UPDATE_ROLE };

            IEnumerable<string> actualReadRolesForCol1 = authZResolver.GetRolesForField(AuthorizationHelpers.TEST_ENTITY, "col1", ActionType.READ);
            CollectionAssert.AreEquivalent(expectedRolesForRead, actualReadRolesForCol1.ToList());
            IEnumerable<string> actualUpdateRolesForCol1 = authZResolver.GetRolesForField(AuthorizationHelpers.TEST_ENTITY, "col1", ActionType.UPDATE);
            CollectionAssert.AreEquivalent(expectedRolesForUpdate, actualUpdateRolesForCol1.ToList());

            IEnumerable<string> actualRolesForRead = authZResolver.GetRolesForAction(AuthorizationHelpers.TEST_ENTITY, ActionType.READ);
            CollectionAssert.AreEquivalent(expectedRolesForRead, actualRolesForRead.ToList());
            IEnumerable<string> actualRolesForUpdate = authZResolver.GetRolesForAction(AuthorizationHelpers.TEST_ENTITY, ActionType.UPDATE);
            CollectionAssert.AreEquivalent(expectedRolesForUpdate, actualRolesForUpdate.ToList());
        }

        /// <summary>
        /// Test to validate that the permissions for authenticated role are derived from anonymous role
        /// when authenticate role is not defined, but anonymous role is defined.
        /// </summary>
        [TestMethod]
        public void TestAuthenticatedRoleWhenAnonymousRoleIsDefined()
        {
            string authenticatedRole = AuthorizationType.Authenticated.ToString().ToLower();
            string anonymousRole = AuthorizationType.Anonymous.ToString().ToLower();

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                anonymousRole,
                ActionType.CREATE);

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Create action should be defined for anonymous role.
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, anonymousRole, ActionType.CREATE));

            // Create action should be defined for authenticated role as well,
            // because it is defined for anonymous role.
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, authenticatedRole, ActionType.CREATE));

            // No other action should be defined for anonymous/authenticated role because of same reasoning.
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, authenticatedRole, ActionType.READ));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, authenticatedRole, ActionType.READ));

            // Anonymous role's permissions are mimiced for authenticated role only.
            // Assert by checking for an arbitrary role.
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE, ActionType.CREATE));
        }

        /// <summary>
        /// Test to validate that the permissions for authenticated role are derived from anonymous role
        /// when authenticate role is not defined, but anonymous role is defined.
        /// </summary>
        [TestMethod]
        public void TestAuthenticatedRoleWhenAnonymousRoleIsNotDefined()
        {
            string authenticatedRole = AuthorizationType.Authenticated.ToString().ToLower();

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE);

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Create action should be defined for anonymous role.
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE, ActionType.CREATE));

            // Create action should not be defined for authenticated role,
            // because neither authenticated nor anonymous role is defined.
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, authenticatedRole, ActionType.CREATE));
        }

        /// <summary>
        /// Test to validate that when anonymous and authenticated role are both defined, then
        /// authenticate role does not derive permission from anonymous role.
        /// </summary>
        [TestMethod]
        public void TestAuthenticatedRoleWhenBothAnonymousAndAuthenticatedAreDefined()
        {
            string authenticatedRole = AuthorizationType.Authenticated.ToString().ToLower();
            string anonymousRole = AuthorizationType.Anonymous.ToString().ToLower();

            Field fieldsForRole = new(
                include: new HashSet<string> { "col1" },
                exclude: null);

            Action readAction = new(
                Name: ActionType.READ,
                Fields: fieldsForRole,
                Policy: null);

            Action updateAction = new(
                Name: ActionType.UPDATE,
                Fields: fieldsForRole,
                Policy: null);

            PermissionSetting authenticatedPermission = new(
                role: authenticatedRole,
                actions: new object[] { JsonSerializer.SerializeToElement(readAction) });

            PermissionSetting anonymousPermission = new(
            role: anonymousRole,
            actions: new object[] { JsonSerializer.SerializeToElement(readAction), JsonSerializer.SerializeToElement(updateAction) });

            Entity sampleEntity = new(
                Source: TEST_ENTITY,
                Rest: null,
                GraphQL: null,
                Permissions: new PermissionSetting[] { authenticatedPermission, anonymousPermission },
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

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Assert that for authenticated role, only read action is allowed and
            // update action is not allowed even though update is allowed for anonymous role.
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, authenticatedRole, ActionType.READ));
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, anonymousRole, ActionType.UPDATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, authenticatedRole, ActionType.UPDATE));
        }
        #endregion

        #region Column Tests

        /// <summary>
        /// Tests the authorization stage: Columns defined for Action
        /// Columns are allowed for role
        /// Columns are not allowed for role
        /// Wildcard included and/or excluded columns handling
        /// and assumes request validation has already occurred
        /// </summary>
        [TestMethod("Explicit include columns with no exclusion")]
        public void ExplicitIncludeColumn()
        {
            HashSet<string> includedColumns = new() { "col1", "col2", "col3" };
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: includedColumns
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, includedColumns));

            // Not allow column.
            //
            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, new List<string> { "col4" }));

            // Mix of allow and not allow. Should result in not allow.
            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, new List<string> { "col3", "col4" }));

            // Column does not exist 
            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, new List<string> { "col5", "col6" }));
        }

        /// <summary>
        /// Test to validate that for wildcard action, the authorization stage for column check
        /// would pass if the action is one among create, read, update, delete and the columns are accessible.
        /// Similarly if the column is in accessible, then we should not have access.
        /// </summary>
        [TestMethod("Explicit include and exclude columns")]
        public void ExplicitIncludeAndExcludeColumns()
        {
            HashSet<string> includeColumns = new() { "col1", "col2" };
            HashSet<string> excludeColumns = new() { "col3" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: includeColumns,
                excludedCols: excludeColumns
                );

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, includeColumns));

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, excludeColumns));

            // Not exist column in the inclusion or exclusion list
            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, new List<string> { "col4" }));

            // Mix of allow and not allow. Should result in not allow.
            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, new List<string> { "col1", "col3" }));
        }

        /// <summary>
        /// Exclusion has precedence over inclusion. So for this test case,
        /// col1 will be excluded even if it is in the inclusion list.
        /// </summary>
        [TestMethod("Same column in exclusion and inclusion list")]
        public void ColumnExclusionWithSameColumnInclusion()
        {
            HashSet<string> includedColumns = new() { "col1", "col2" };
            HashSet<string> excludedColumns = new() { "col1", "col4" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: includedColumns,
                excludedCols: excludedColumns
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Col2 should be included.
            //
            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, new List<string> { "col2" }));

            // Col1 should NOT to included since it is in exclusion list.
            //
            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, new List<string> { "col1" }));

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, excludedColumns));
        }

        /// <summary>
        /// Test that wildcard inclusion will include all the columns in the table.
        /// </summary>
        [TestMethod("Wildcard included columns")]
        public void WildcardColumnInclusion()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            List<string> includedColumns = new() { "col1", "col2", "col3", "col4" };

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, includedColumns));
        }

        /// <summary>
        /// Test that wildcard inclusion will include all column except column specify in exclusion.
        /// Exclusion has priority over inclusion.
        /// </summary>
        [TestMethod("Wildcard include columns with some column exclusion")]
        public void WildcardColumnInclusionWithExplictExclusion()
        {
            List<string> includedColumns = new() { "col1", "col2" };
            HashSet<string> excludedColumns = new() { "col3", "col4" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new HashSet<string> { AuthorizationResolver.WILDCARD },
                excludedCols: excludedColumns
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, includedColumns));
            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, excludedColumns));
        }

        /// <summary>
        /// Test that all columns should be excluded if the exclusion contains wildcard character.
        /// </summary>
        [TestMethod("Wildcard column exclusion")]
        public void WildcardColumnExclusion()
        {
            HashSet<string> excludedColumns = new() { "col1", "col2", "col3", "col4" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                excludedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, excludedColumns));
        }

        /// <summary>
        /// For this test, exclusion has precedence over inclusion. So all columns will be excluded
        /// because wildcard is specified in the exclusion list.
        /// </summary>
        [TestMethod("Wildcard column exclusion with some explicit columns inclusion")]
        public void WildcardColumnExclusionWithExplicitColumnInclusion()
        {
            HashSet<string> includedColumns = new() { "col1", "col2" };
            HashSet<string> excludedColumns = new() { "col3", "col4" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: includedColumns,
                excludedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, includedColumns));
            Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, excludedColumns));
        }

        /// <summary>
        /// Test to validate that for wildcard action, the authorization stage for column check
        /// would pass if the action is one among create, read, update, delete and the columns are accessible.
        /// Similarly if the column is in accessible, then we should not have access.
        /// </summary>
        [TestMethod("Explicit include and exclude columns with wildcard actions")]
        public void CheckIncludeAndExcludeColumnForWildcardAction()
        {
            HashSet<string> includeColumns = new() { "col1", "col2" };
            HashSet<string> excludeColumns = new() { "col3" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                AuthorizationResolver.WILDCARD,
                includedCols: includeColumns,
                excludedCols: excludeColumns
                );

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            foreach (string actionName in RuntimeConfigValidator.ValidActions)
            {
                // Validate that the authorization check passes for valid CRUD actions
                // because columns are accessbile or inaccessible.
                Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, actionName, includeColumns));
                Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, actionName, excludeColumns));
            }
        }

        /// <summary>
        /// Test to validate that the column permissions for authenticated role are derived from anonymous role
        /// when authenticate role is not defined, but anonymous role is defined.
        /// </summary>
        [TestMethod]
        public void TestAuthenticatedRoleForColumnPermissionsWhenAnonymousRoleIsDefined()
        {
            string authenticatedRole = AuthorizationType.Authenticated.ToString().ToLower();
            string anonymousRole = AuthorizationType.Anonymous.ToString().ToLower();

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                anonymousRole,
                AuthorizationResolver.WILDCARD,
                includedCols: new HashSet<string> { "col1" });

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            foreach (string actionName in RuntimeConfigValidator.ValidActions)
            {
                // col1 should be accessible for anonymous role.
                Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, anonymousRole,
                    actionName, new List<string> { "col1" }));

                // col1 should be accessible for authenticated role as well,
                // because it is defined for anonymous role.
                Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, authenticatedRole,
                    actionName, new List<string> { "col1" }));
            }
        }

        #endregion

        #region Tests to validate Database policy parsing
        /// <summary>
        /// Validates the policy parsing logic by asserting that the parsed policy matches the expectedParsedPolicy.
        /// </summary>
        /// <param name="policy">The policy to be parsed.</param>
        /// <param name="expectedParsedPolicy">The policy which is expected to be generated after parsing.</param>
        [DataTestMethod]
        [DataRow("@claims.user_email ne @item.col1 and @claims.contact_no eq @item.col2 and not(@claims.name eq @item.col3)",
            "('xyz@microsoft.com') ne col1 and (1234) eq col2 and not(('Aaron') eq col3)", DisplayName = "Valid policy parsing test 1")]
        [DataRow("(@claims.isemployee eq @item.col1 and @item.col2 ne @claims.user_email) or" +
            " ('David' ne @item.col3 and @claims.contact_no ne @item.col3)", "((true) eq col1 and col2 ne ('xyz@microsoft.com')) or" +
            " ('David' ne col3 and (1234) ne col3)", DisplayName = "Valid policy parsing test 2")]
        [DataRow("(@item.rating gt @claims.emprating) and (@claims.isemployee eq true)",
            "(rating gt (4.2)) and ((true) eq true)", DisplayName = "Valid policy parsing test 3")]
        [DataRow("@item.rating eq @claims.emprating)", "rating eq (4.2))", DisplayName = "Valid policy parsing test 4")]
        public void ParseValidDbPolicy(string policy, string expectedParsedPolicy)
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: policy
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Mock<HttpContext> context = new();

            //Add identity object to the Mock context object.
            ClaimsIdentity identity = new(TEST_AUTHENTICATION_TYPE, TEST_CLAIMTYPE_NAME, TEST_ROLE_TYPE);
            identity.AddClaim(new Claim("user_email", "xyz@microsoft.com", ClaimValueTypes.String));
            identity.AddClaim(new Claim("name", "Aaron", ClaimValueTypes.String));
            identity.AddClaim(new Claim("contact_no", "1234", ClaimValueTypes.Integer64));
            identity.AddClaim(new Claim("isemployee", "true", ClaimValueTypes.Boolean));
            identity.AddClaim(new Claim("emprating", "4.2", ClaimValueTypes.Double));
            ClaimsPrincipal principal = new(identity);
            context.Setup(x => x.User).Returns(principal);
            context.Setup(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(TEST_ROLE);

            string parsedPolicy = authZResolver.TryProcessDBPolicy(TEST_ENTITY, TEST_ROLE, TEST_ACTION, context.Object);
            Assert.AreEqual(parsedPolicy, expectedParsedPolicy);
        }

        /// <summary>
        /// Test to validate that we are correctly throwing an appropriate exception when the user request
        /// lacks a claim required by the policy.
        /// </summary>
        /// <param name="policy">The policy to be parsed.</param>
        [DataTestMethod]
        [DataRow("@claims.user_email eq @item.col1 and @claims.emprating eq @item.rating",
            DisplayName = "'emprating' claim missing from request")]
        [DataRow("@claims.user_email eq @item.col1 and not ( true eq @claims.isemployee or @claims.name eq 'Aaron')",
            DisplayName = "'name' claim missing from request")]
        public void ParseInvalidDbPolicyWithUserNotPossessingAllClaims(string policy)
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: policy
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Mock<HttpContext> context = new();

            //Add identity object to the Mock context object.
            ClaimsIdentity identity = new(TEST_AUTHENTICATION_TYPE, TEST_CLAIMTYPE_NAME, TEST_ROLE_TYPE);
            identity.AddClaim(new Claim("user_email", "xyz@microsoft.com", ClaimValueTypes.String));
            identity.AddClaim(new Claim("isemployee", "true", ClaimValueTypes.Boolean));
            ClaimsPrincipal principal = new(identity);
            context.Setup(x => x.User).Returns(principal);
            context.Setup(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(TEST_ROLE);

            try
            {
                authZResolver.TryProcessDBPolicy(TEST_ENTITY, TEST_ROLE, TEST_ACTION, context.Object);
            }
            catch (DataGatewayException ex)
            {
                Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
                Assert.AreEqual("User does not possess all the claims required to perform this action.", ex.Message);
            }
        }

        /// <summary>
        /// Test to validate that duplicate claims throws an exception for everything except roles
        /// duplicate role claims are ignored, so just checks policy is parsed as expected in this case 
        /// </summary>
        /// <param name="exceptionExpected"> Whether we expect an exception (403 forbidden) to be thrown while parsing policy </param>
        /// <param name="claims"> Parameter list of claim types/keys to add to the claims dictionary that can be accessed with @claims </param>
        [DataTestMethod]
        [DataRow(true, ClaimTypes.Role, "username", "guid", "username", DisplayName = "duplicate claim expect exception")]
        [DataRow(false, ClaimTypes.Role, "username", "guid", ClaimTypes.Role, DisplayName = "duplicate role claim does not expect exception")]
        [DataRow(true, ClaimTypes.Role, ClaimTypes.Role, "username", "username", DisplayName = "duplicate claim expect exception ignoring role")]
        public void ParsePolicyWithDuplicateUserClaims(bool exceptionExpected, params string[] claimTypes)
        {
            string policy = $"@claims.guid eq 1";
            string defaultClaimValue = "unimportant";
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_ACTION,
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                databasePolicy: policy
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);
            Mock<HttpContext> context = new();

            //Add identity object to the Mock context object.
            ClaimsIdentity identity = new(TEST_AUTHENTICATION_TYPE, TEST_CLAIMTYPE_NAME, TEST_ROLE_TYPE);
            foreach (string claimType in claimTypes)
            {
                identity.AddClaim(new Claim(type: claimType, value: defaultClaimValue, ClaimValueTypes.String));
            }

            ClaimsPrincipal principal = new(identity);
            context.Setup(x => x.User).Returns(principal);
            context.Setup(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(TEST_ROLE);

            // We expect an exception if duplicate claims are present EXCEPT for role claim
            if (exceptionExpected)
            {
                try
                {
                    authZResolver.TryProcessDBPolicy(TEST_ENTITY, TEST_ROLE, TEST_ACTION, context.Object);
                    Assert.Fail();
                }
                catch (DataGatewayException ex)
                {
                    Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
                    Assert.AreEqual("Duplicate claims are not allowed within a request.", ex.Message);
                }
            }
            else
            {
                // If the role claim was the only duplicate, simply verify policy parsed as expected
                string expectedPolicy = $"('{defaultClaimValue}') eq 1";
                string parsedPolicy = authZResolver.TryProcessDBPolicy(TEST_ENTITY, TEST_ROLE, TEST_ACTION, context.Object);
                Assert.AreEqual(expected: expectedPolicy, actual: parsedPolicy);
            }
        }
        #endregion

        #region Helpers
        public static RuntimeConfig InitRuntimeConfig(
            string entityName = "SampleEntity",
            string roleName = "Reader",
            string actionName = ActionType.CREATE,
            HashSet<string>? includedCols = null,
            HashSet<string>? excludedCols = null,
            string? requestPolicy = null,
            string? databasePolicy = null
            )
        {
            Field fieldsForRole = new(
                include: includedCols,
                exclude: excludedCols);

            Policy policy = new(
                request: requestPolicy,
                database: databasePolicy);

            Action actionForRole = new(
                Name: actionName,
                Fields: fieldsForRole,
                Policy: policy);

            PermissionSetting permissionForEntity = new(
                role: roleName,
                actions: new object[] { JsonSerializer.SerializeToElement(actionForRole) });

            Entity sampleEntity = new(
                Source: TEST_ENTITY,
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

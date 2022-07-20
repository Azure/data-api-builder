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
            foreach (string actionName in RuntimeConfigValidator._validActions)
            {
                Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, actionName));

                IEnumerable<string> actualRolesForCol1 = authZResolver.GetRolesForField(AuthorizationHelpers.TEST_ENTITY, "col1", actionName);

                CollectionAssert.AreEquivalent(expectedRoles, actualRolesForCol1.ToList());
            }

            IEnumerable<string> actualRolesForAction = authZResolver.GetRolesForAction(AuthorizationHelpers.TEST_ENTITY, ActionType.CREATE);
            CollectionAssert.AreEquivalent(expectedRoles, actualRolesForAction.ToList());

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
            const string readOnlyRole = "readOnlyRole";
            const string readAndUpdateRole = "readAndUpdateRole";

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
                role: readOnlyRole,
                actions: new object[] { JsonSerializer.SerializeToElement(readAction) });

            PermissionSetting readAndUpdatePermission = new(
            role: readAndUpdateRole,
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
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, readOnlyRole, ActionType.READ));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, readOnlyRole, ActionType.UPDATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, readOnlyRole, ActionType.CREATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, readOnlyRole, ActionType.DELETE));

            // Verify that read only role has permission for read and nothing else.
            //
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, readAndUpdateRole, ActionType.READ));
            Assert.IsTrue(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, readAndUpdateRole, ActionType.UPDATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, readAndUpdateRole, ActionType.CREATE));
            Assert.IsFalse(authZResolver.AreRoleAndActionDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, readAndUpdateRole, ActionType.DELETE));

            List<string> expectedRolesForRead = new() { readOnlyRole, readAndUpdateRole };
            List<string> expectedRolesForUpdate = new() { readAndUpdateRole };

            IEnumerable<string> actualReadRolesForCol1 = authZResolver.GetRolesForField(AuthorizationHelpers.TEST_ENTITY, "col1", ActionType.READ);
            CollectionAssert.AreEquivalent(expectedRolesForRead, actualReadRolesForCol1.ToList());
            IEnumerable<string> actualUpdateRolesForCol1 = authZResolver.GetRolesForField(AuthorizationHelpers.TEST_ENTITY, "col1", ActionType.UPDATE);
            CollectionAssert.AreEquivalent(expectedRolesForUpdate, actualUpdateRolesForCol1.ToList());

            IEnumerable<string> actualRolesForRead = authZResolver.GetRolesForAction(AuthorizationHelpers.TEST_ENTITY, ActionType.READ);
            CollectionAssert.AreEquivalent(expectedRolesForRead, actualRolesForRead.ToList());
            IEnumerable<string> actualRolesForUpdate = authZResolver.GetRolesForAction(AuthorizationHelpers.TEST_ENTITY, ActionType.UPDATE);
            CollectionAssert.AreEquivalent(expectedRolesForUpdate, actualRolesForUpdate.ToList());
        }

        #endregion

        #region Positive Column Tests

        /// <summary>
        /// Tests the authorization stage: Columns defined for Action
        /// Columns are allowed for role
        /// Columns are not allowed for role
        /// Wildcard included and/or excluded columns handling
        /// and assumes request validation has already occurred
        /// </summary>
        [TestMethod("Column allowed for action on role")]
        public void ColsDefinedForAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE,
                includedCols: new HashSet<string> { "col1", "col2", "col3" }
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
                includedCols: new HashSet<string> { "col1", "col2", "col3" }
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
                includedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
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
                includedCols: new HashSet<string> { AuthorizationResolver.WILDCARD },
                excludedCols: new HashSet<string> { "col1", "col2" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with column allowed.
            List<string> columns = new(new string[] { "col3", "col4" });
            bool expected = true;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        /// <summary>
        /// Test to validate that for wildcard action, the authorization stage for column check
        /// would pass if the action is one among create, read, update, delete and the columns are accessible.
        /// </summary>
        [TestMethod]
        public void AreColumnsAllowedForActionViaWildcardAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                AuthorizationResolver.WILDCARD,
                includedCols: new HashSet<string> { "col1", "col2" },
                excludedCols: new HashSet<string> { "col3" }
                );

            // Mock Request Values - Query a configured entity/role/action with column allowed.
            List<string> columns = new(new string[] { "col1", "col2" });
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            foreach (string actionName in RuntimeConfigValidator._validActions)
            {
                // Validate that the authorization check passes for valid CRUD actions
                // because columns are accessbile.
                Assert.IsTrue(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, actionName, columns));
            }
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
                includedCols: new HashSet<string> { "col1", "col2", "col3" }
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
                includedCols: new HashSet<string> { "col1", "col2", "col3" }
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
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                excludedCols: new HashSet<string> { "col4", "col5", "col6" }
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
                includedCols: new HashSet<string> { "col1", "col2", "col3" },
                excludedCols: new HashSet<string> { "col4", "col5", "col6" }
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
                excludedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
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
                includedCols: new HashSet<string> { AuthorizationResolver.WILDCARD },
                excludedCols: new HashSet<string> { "col1", "col2" }
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
                includedCols: new HashSet<string> { "col1", "col2" },
                excludedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
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
                includedCols: new HashSet<string> { "col1", "col2" },
                excludedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            List<string> columns = new(new string[] { "col1", "col2" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
        }

        /// <summary>
        /// Test to validate that for wildcard action, the authorization stage for column check
        /// would fail even if the action is one among create, read, update, delete and the columns are inaccessible.
        /// </summary>
        [TestMethod]
        public void InaccessibleColumnsForActionViaWildcardAction_ColsForActionTest()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                AuthorizationResolver.WILDCARD,
                includedCols: new HashSet<string> { "col1", "col2" },
                excludedCols: new HashSet<string> { "col3" }
                );

            // Mock Request Values - Query a configured entity/role/action with column not allowed.
            List<string> columns = new(new string[] { "col1", "col3" });
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            foreach (string actionName in RuntimeConfigValidator._validActions)
            {
                // Validate that the authorization check fails as the some columns are inaccessible.
                Assert.IsFalse(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, actionName, columns));
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

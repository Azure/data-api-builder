#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
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
                includedCols: new HashSet<string> { "*" }
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
                includedCols: new HashSet<string> { "*" },
                excludedCols: new HashSet<string> { "col1", "col2" }
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
                excludedCols: new HashSet<string> { "*" }
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
                includedCols: new HashSet<string> { "*" },
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
                excludedCols: new HashSet<string> { "*" }
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
                excludedCols: new HashSet<string> { "*" }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values - Query a configured entity/role/action with columns allowed.
            List<string> columns = new(new string[] { "col1", "col2" });
            bool expected = false;

            Assert.AreEqual(authZResolver.AreColumnsAllowedForAction(AuthorizationHelpers.TEST_ENTITY, AuthorizationHelpers.TEST_ROLE, ActionType.CREATE, columns), expected);
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

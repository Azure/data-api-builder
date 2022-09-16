#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PermissionOperation = Azure.DataApiBuilder.Config.PermissionOperation;

namespace Azure.DataApiBuilder.Service.Tests.Authorization
{
    [TestClass]
    public class AuthorizationResolverUnitTests
    {
        private const string TEST_ENTITY = "SampleEntity";
        private const string TEST_ROLE = "Writer";
        private const Operation TEST_OPERATION = Operation.Create;
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

        #region Role and Operation on Entity Tests

        /// <summary>
        /// Tests the AreRoleAndOperationDefinedForEntity stage of authorization.
        /// Request operation is defined for role -> VALID
        /// Request operation not defined for role (role has 0 defined operations)
        ///     Ensures method short ciruits in circumstances role is not defined -> INVALID
        /// Request operation does not match an operation defined for role (role has >=1 defined operation) -> INVALID
        /// </summary>
        [DataTestMethod]
        [DataRow("Writer", Operation.Create, "Writer", Operation.Create, true)]
        [DataRow("Reader", Operation.Create, "Reader", Operation.None, false)]
        [DataRow("Writer", Operation.Create, "Writer", Operation.Update, false)]
        public void AreRoleAndOperationDefinedForEntityTest(
            string configRole,
            Operation configOperation,
            string roleName,
            Operation operation,
            bool expected)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: configRole,
                operation: configOperation);
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Mock Request Values
            Assert.AreEqual(expected, authZResolver.AreRoleAndOperationDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, roleName, operation));
        }

        /// <summary>
        /// Test that wildcard operation are expanded to explicit operations.
        /// Verifies that internal data structure are created correctly.
        /// </summary>
        [TestMethod("Wildcard operation is expanded to all valid operations")]
        public void TestWildcardOperation()
        {
            List<string> expectedRoles = new() { AuthorizationHelpers.TEST_ROLE };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.All);

            // Override the permission operations to be a list of operations for wildcard
            // instead of a list of objects created by InitRuntimeConfig()
            runtimeConfig.Entities[AuthorizationHelpers.TEST_ENTITY].Permissions[0].Operations =
                new object[] { JsonSerializer.SerializeToElement(AuthorizationResolver.WILDCARD) };
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // There should not be a wildcard operation in AuthorizationResolver.EntityPermissionsMap
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.All));

            // The wildcard operation should be expanded to all the explicit operations.
            foreach (Operation operation in PermissionOperation.ValidPermissionOperations)
            {
                Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(
                    AuthorizationHelpers.TEST_ENTITY,
                    AuthorizationHelpers.TEST_ROLE,
                    operation));

                IEnumerable<string> actualRolesForCol1 = authZResolver.GetRolesForField(AuthorizationHelpers.TEST_ENTITY, "col1", operation);

                CollectionAssert.AreEquivalent(expectedRoles, actualRolesForCol1.ToList());

                IEnumerable<string> actualRolesForOperation = IAuthorizationResolver.GetRolesForOperation(
                    AuthorizationHelpers.TEST_ENTITY,
                    operation,
                    authZResolver.EntityPermissionsMap);
                CollectionAssert.AreEquivalent(expectedRoles, actualRolesForOperation.ToList());
            }

            // Validate that the authorization check fails because the operations are invalid.
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, TEST_ROLE, Operation.Insert));
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, TEST_ROLE, Operation.Upsert));
        }

        /// <summary>
        /// Verify that the internal data structure is created correctly when we have
        /// Two roles for the same entity with different permission.
        /// readOnlyRole - Read permission only for col1 and no policy.
        /// readAndUpdateRole - read and update permission for col1 and no policy.
        /// </summary>
        [TestMethod]
        public void TestRoleAndOperationCombination()
        {
            const string READ_ONLY_ROLE = "readOnlyRole";
            const string READ_AND_UPDATE_ROLE = "readAndUpdateRole";

            Field fieldsForRole = new(
                include: new HashSet<string> { "col1" },
                exclude: null);

            PermissionOperation readAction = new(
                Name: Operation.Read,
                Fields: fieldsForRole,
                Policy: null);

            PermissionOperation updateAction = new(
                Name: Operation.Update,
                Fields: fieldsForRole,
                Policy: null);

            PermissionSetting readOnlyPermission = new(
                role: READ_ONLY_ROLE,
                operations: new object[] { JsonSerializer.SerializeToElement(readAction) });

            PermissionSetting readAndUpdatePermission = new(
            role: READ_AND_UPDATE_ROLE,
            operations: new object[] { JsonSerializer.SerializeToElement(readAction), JsonSerializer.SerializeToElement(updateAction) });

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
            Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                READ_ONLY_ROLE,
                Operation.Read));
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                READ_ONLY_ROLE,
                Operation.Update));
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                READ_ONLY_ROLE,
                Operation.Create));
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                READ_ONLY_ROLE,
                Operation.Delete));

            // Verify that read only role has permission for read/update and nothing else.
            Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                READ_AND_UPDATE_ROLE,
                Operation.Read));
            Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                READ_AND_UPDATE_ROLE,
                Operation.Update));
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                READ_AND_UPDATE_ROLE,
                Operation.Create));
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                READ_AND_UPDATE_ROLE,
                Operation.Delete));

            List<string> expectedRolesForRead = new() { READ_ONLY_ROLE, READ_AND_UPDATE_ROLE };
            List<string> expectedRolesForUpdate = new() { READ_AND_UPDATE_ROLE };

            IEnumerable<string> actualReadRolesForCol1 = authZResolver.GetRolesForField(
                AuthorizationHelpers.TEST_ENTITY,
                "col1",
                Operation.Read);
            CollectionAssert.AreEquivalent(expectedRolesForRead, actualReadRolesForCol1.ToList());
            IEnumerable<string> actualUpdateRolesForCol1 = authZResolver.GetRolesForField(
                AuthorizationHelpers.TEST_ENTITY,
                "col1",
                Operation.Update);
            CollectionAssert.AreEquivalent(expectedRolesForUpdate, actualUpdateRolesForCol1.ToList());

            IEnumerable<string> actualRolesForRead = IAuthorizationResolver.GetRolesForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                Operation.Read,
                authZResolver.EntityPermissionsMap);
            CollectionAssert.AreEquivalent(expectedRolesForRead, actualRolesForRead.ToList());
            IEnumerable<string> actualRolesForUpdate = IAuthorizationResolver.GetRolesForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                Operation.Update,
                authZResolver.EntityPermissionsMap);
            CollectionAssert.AreEquivalent(expectedRolesForUpdate, actualRolesForUpdate.ToList());
        }

        /// <summary>
        /// Test to validate that the permissions for the system role "authenticated" are derived the permissions of
        /// the system role "anonymous" when authenticated role is not defined, but anonymous role is defined.
        /// </summary>
        [TestMethod]
        public void TestAuthenticatedRoleWhenAnonymousRoleIsDefined()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationResolver.ROLE_ANONYMOUS,
                operation: Operation.Create);

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            foreach (Operation operation in PermissionOperation.ValidPermissionOperations)
            {
                if (operation is Operation.Create)
                {
                    // Create operation should be defined for anonymous role.
                    Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(
                        AuthorizationHelpers.TEST_ENTITY,
                        AuthorizationResolver.ROLE_ANONYMOUS,
                        operation));

                    // Create operation should be defined for authenticated role as well,
                    // because it is defined for anonymous role.
                    Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(
                        AuthorizationHelpers.TEST_ENTITY,
                        AuthorizationResolver.ROLE_AUTHENTICATED,
                        operation));
                }
                else
                {
                    // Check that no other operation is defined for the authenticated role to ensure
                    // the authenticated role's permissions match that of the anonymous role's permissions.
                    Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                        AuthorizationHelpers.TEST_ENTITY,
                        AuthorizationResolver.ROLE_AUTHENTICATED,
                        operation));
                    Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                        AuthorizationHelpers.TEST_ENTITY,
                        AuthorizationResolver.ROLE_ANONYMOUS,
                        operation));
                }
            }

            // Anonymous role's permissions are copied over for authenticated role only.
            // Assert by checking for an arbitrary role.
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE, Operation.Create));

            // Assert that the create operation has both anonymous, authenticated roles.
            List<string> expectedRolesForCreate = new() { AuthorizationResolver.ROLE_AUTHENTICATED, AuthorizationResolver.ROLE_ANONYMOUS };
            IEnumerable<string> actualRolesForCreate = IAuthorizationResolver.GetRolesForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                Operation.Create,
                authZResolver.EntityPermissionsMap);
            CollectionAssert.AreEquivalent(expectedRolesForCreate, actualRolesForCreate.ToList());

            // Assert that the col1 field with create operation has both anonymous, authenticated roles.
            List<string> expectedRolesForCreateCol1 = new() {
                AuthorizationResolver.ROLE_ANONYMOUS,
                AuthorizationResolver.ROLE_AUTHENTICATED };
            IEnumerable<string> actualRolesForCreateCol1 = authZResolver.GetRolesForField(
                AuthorizationHelpers.TEST_ENTITY,
                "col1", Operation.Create);
            CollectionAssert.AreEquivalent(expectedRolesForCreateCol1, actualRolesForCreateCol1.ToList());

            // Assert that the col1 field with read operation has no role.
            List<string> expectedRolesForReadCol1 = new();
            IEnumerable<string> actualRolesForReadCol1 = authZResolver.GetRolesForField(
                AuthorizationHelpers.TEST_ENTITY,
                "col1", Operation.Read);
            CollectionAssert.AreEquivalent(expectedRolesForReadCol1, actualRolesForReadCol1.ToList());
        }

        /// <summary>
        /// Test to validate that the no permissions for authenticated role are derived when
        /// both anonymous and authenticated role are not defined.
        /// </summary>
        [TestMethod]
        public void TestAuthenticatedRoleWhenAnonymousRoleIsNotDefined()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create);

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Create operation should be defined for test role.
            Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create));

            // Create operation should not be defined for authenticated role,
            // because neither authenticated nor anonymous role is defined.
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationResolver.ROLE_AUTHENTICATED,
                Operation.Create));

            // Assert that the Create operation has only test_role.
            List<string> expectedRolesForCreate = new() { AuthorizationHelpers.TEST_ROLE };
            IEnumerable<string> actualRolesForCreate = IAuthorizationResolver.GetRolesForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                Operation.Create,
                authZResolver.EntityPermissionsMap);
            CollectionAssert.AreEquivalent(expectedRolesForCreate, actualRolesForCreate.ToList());

            // Since neither anonymous nor authenticated role is defined for the entity,
            // Create operation would only have the test_role.
            List<string> expectedRolesForCreateCol1 = new() { AuthorizationHelpers.TEST_ROLE };
            IEnumerable<string> actualRolesForCreateCol1 = authZResolver.GetRolesForField(
                AuthorizationHelpers.TEST_ENTITY,
                "col1", Operation.Create);
            CollectionAssert.AreEquivalent(expectedRolesForCreateCol1, actualRolesForCreateCol1.ToList());
        }

        /// <summary>
        /// Test to validate that when anonymous and authenticated role are both defined, then
        /// the authenticated role does not derive permissions from anonymous role's permissions.
        /// </summary>
        [TestMethod]
        public void TestAuthenticatedRoleWhenBothAnonymousAndAuthenticatedAreDefined()
        {
            Field fieldsForRole = new(
                include: new HashSet<string> { "col1" },
                exclude: null);

            PermissionOperation readAction = new(
                Name: Operation.Read,
                Fields: fieldsForRole,
                Policy: null);

            PermissionOperation updateAction = new(
                Name: Operation.Update,
                Fields: fieldsForRole,
                Policy: null);

            PermissionSetting authenticatedPermission = new(
                role: AuthorizationResolver.ROLE_AUTHENTICATED,
                operations: new object[] { JsonSerializer.SerializeToElement(readAction) });

            PermissionSetting anonymousPermission = new(
            role: AuthorizationResolver.ROLE_ANONYMOUS,
            operations: new object[] { JsonSerializer.SerializeToElement(readAction), JsonSerializer.SerializeToElement(updateAction) });

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

            // Assert that for the role authenticated, only the Read operation is allowed.
            // The Update operation is not allowed even though update is allowed for the role anonymous.
            Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(AuthorizationHelpers.TEST_ENTITY,
                AuthorizationResolver.ROLE_AUTHENTICATED, Operation.Read));
            Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(AuthorizationHelpers.TEST_ENTITY,
                AuthorizationResolver.ROLE_ANONYMOUS, Operation.Update));
            Assert.IsFalse(authZResolver.AreRoleAndOperationDefinedForEntity(AuthorizationHelpers.TEST_ENTITY,
                AuthorizationResolver.ROLE_AUTHENTICATED, Operation.Delete));

            // Assert that the read operation has both anonymous and authenticated role.
            List<string> expectedRolesForRead = new() {
                AuthorizationResolver.ROLE_ANONYMOUS,
                AuthorizationResolver.ROLE_AUTHENTICATED };
            IEnumerable<string> actualRolesForRead = IAuthorizationResolver.GetRolesForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                Operation.Read,
                authZResolver.EntityPermissionsMap);
            CollectionAssert.AreEquivalent(expectedRolesForRead, actualRolesForRead.ToList());

            // Assert that the update operation has only anonymous role.
            List<string> expectedRolesForUpdate = new() { AuthorizationResolver.ROLE_ANONYMOUS };
            IEnumerable<string> actualRolesForUpdate = IAuthorizationResolver.GetRolesForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                Operation.Update,
                authZResolver.EntityPermissionsMap);
            CollectionAssert.AreEquivalent(expectedRolesForUpdate, actualRolesForUpdate.ToList());

            // Assert that the col1 field with Read operation has both anonymous and authenticated roles.
            List<string> expectedRolesForReadCol1 = new() {
                AuthorizationResolver.ROLE_ANONYMOUS,
                AuthorizationResolver.ROLE_AUTHENTICATED };
            IEnumerable<string> actualRolesForReadCol1 = authZResolver.GetRolesForField(
                AuthorizationHelpers.TEST_ENTITY,
                "col1", Operation.Read);
            CollectionAssert.AreEquivalent(expectedRolesForReadCol1, actualRolesForReadCol1.ToList());

            // Assert that the col1 field with Update operation has only anonymous roles.
            List<string> expectedRolesForUpdateCol1 = new() { AuthorizationResolver.ROLE_ANONYMOUS };
            IEnumerable<string> actualRolesForUpdateCol1 = authZResolver.GetRolesForField(
                AuthorizationHelpers.TEST_ENTITY,
                "col1", Operation.Update);
            CollectionAssert.AreEquivalent(expectedRolesForUpdateCol1, actualRolesForUpdateCol1.ToList());
        }

        /// <summary>
        /// Test to validate the AreRoleAndOperationDefinedForEntity method for the case insensitivity of roleName.
        /// For eg. The role Writer is equivalent to wrIter, wRITer, WRITER etc.
        /// </summary>
        /// <param name="configRole">The role configured on the entity.</param>
        /// <param name="operation">The operation configured for the configRole.</param>
        /// <param name="roleNameToCheck">The roleName which is to be checked for the permission.</param>
        [DataTestMethod]
        [DataRow("Writer", Operation.Create, "wRiTeR", DisplayName = "role wRiTeR checked against Writer")]
        [DataRow("Reader", Operation.Read, "READER", DisplayName = "role READER checked against Reader")]
        [DataRow("Writer", Operation.Create, "WrIter", DisplayName = "role WrIter checked against Writer")]
        public void AreRoleAndOperationDefinedForEntityTestForDifferentlyCasedRole(
            string configRole,
            Operation operation,
            string roleNameToCheck
            )
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: configRole,
                operation: operation);
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Assert that the roleName is case insensitive.
            Assert.IsTrue(authZResolver.AreRoleAndOperationDefinedForEntity(AuthorizationHelpers.TEST_ENTITY, roleNameToCheck, operation));
        }
        #endregion

        #region Column Tests

        /// <summary>
        /// Tests the authorization stage: Columns defined for operation
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
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedCols: includedColumns
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsTrue(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                includedColumns));

            // Not allow column.
            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                new List<string> { "col4" }));

            // Mix of allow and not allow. Should result in not allow.
            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                new List<string> { "col3", "col4" }));

            // Column does not exist 
            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                new List<string> { "col5", "col6" }));
        }

        /// <summary>
        /// Test to validate that for wildcard operation, the authorization stage for column check
        /// would pass if the operation is one among create, read, update, delete and the columns are accessible.
        /// Similarly if the column is in accessible, then we should not have access.
        /// </summary>
        [TestMethod("Explicit include and exclude columns")]
        public void ExplicitIncludeAndExcludeColumns()
        {
            HashSet<string> includeColumns = new() { "col1", "col2" };
            HashSet<string> excludeColumns = new() { "col3" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedCols: includeColumns,
                excludedCols: excludeColumns
                );

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsTrue(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                includeColumns));

            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                excludeColumns));

            // Not exist column in the inclusion or exclusion list
            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                new List<string> { "col4" }));

            // Mix of allow and not allow. Should result in not allow.
            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                Operation.Create,
                new List<string> { "col1", "col3" }));
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
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedCols: includedColumns,
                excludedCols: excludedColumns
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Col2 should be included.
            //
            Assert.IsTrue(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                new List<string> { "col2" }));

            // Col1 should NOT to included since it is in exclusion list.
            //
            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                new List<string> { "col1" }));

            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                excludedColumns));
        }

        /// <summary>
        /// Test that wildcard inclusion will include all the columns in the table.
        /// </summary>
        [TestMethod("Wildcard included columns")]
        public void WildcardColumnInclusion()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            List<string> includedColumns = new() { "col1", "col2", "col3", "col4" };

            Assert.IsTrue(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedColumns));
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
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedCols: new HashSet<string> { AuthorizationResolver.WILDCARD },
                excludedCols: excludedColumns
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsTrue(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedColumns));
            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                excludedColumns));
        }

        /// <summary>
        /// Test that all columns should be excluded if the exclusion contains wildcard character.
        /// </summary>
        [TestMethod("Wildcard column exclusion")]
        public void WildcardColumnExclusion()
        {
            HashSet<string> excludedColumns = new() { "col1", "col2", "col3", "col4" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                excludedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                excludedColumns));
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
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedCols: includedColumns,
                excludedCols: new HashSet<string> { AuthorizationResolver.WILDCARD }
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                includedColumns));
            Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create,
                excludedColumns));
        }

        /// <summary>
        /// Test to validate that for wildcard operation, the authorization stage for column check
        /// would pass if the operation is one among create, read, update, delete and the columns are accessible.
        /// Similarly if the column is in accessible, then we should not have access.
        /// </summary>
        [TestMethod("Explicit include and exclude columns with wildcard operation")]
        public void CheckIncludeAndExcludeColumnForWildcardOperation()
        {
            HashSet<string> includeColumns = new() { "col1", "col2" };
            HashSet<string> excludeColumns = new() { "col3" };

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.All,
                includedCols: includeColumns,
                excludedCols: excludeColumns
                );

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            foreach (Operation operation in PermissionOperation.ValidPermissionOperations)
            {
                // Validate that the authorization check passes for valid CRUD operations
                // because columns are accessbile or inaccessible.
                Assert.IsTrue(authZResolver.AreColumnsAllowedForOperation(
                    AuthorizationHelpers.TEST_ENTITY,
                    AuthorizationHelpers.TEST_ROLE,
                    operation,
                    includeColumns));
                Assert.IsFalse(authZResolver.AreColumnsAllowedForOperation(
                    AuthorizationHelpers.TEST_ENTITY,
                    AuthorizationHelpers.TEST_ROLE,
                    operation,
                    excludeColumns));
            }
        }

        /// <summary>
        /// Test to validate that when Field property is missing from the operation, all the columns present in
        /// the table are treated as accessible. Since we are not explicitly specifying the includeCols/excludedCols
        /// parameters when initializing the RuntimeConfig, Field will be nullified.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, "col1", "col2", DisplayName = "Accessible fields col1,col2")]
        [DataRow(true, "col3", "col4", DisplayName = "Accessible fields col3,col4")]
        [DataRow(false, "col5", DisplayName = "Inaccessible field col5")]
        public void AreColumnsAllowedForOperationWithMissingFieldProperty(bool expected, params string[] columnsToCheck)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationHelpers.TEST_ROLE,
                operation: Operation.Create
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            // Assert that the expected result and the returned result are equal.
            // The entity is expected to have "col1", "col2", "col3", "col4" fields accessible on it.
            Assert.AreEqual(expected,
                authZResolver.AreColumnsAllowedForOperation(
                    AuthorizationHelpers.TEST_ENTITY,
                    AuthorizationHelpers.TEST_ROLE,
                    Operation.Create,
                    new List<string>(columnsToCheck)));
        }

        /// <summary>
        /// Test to validate that the column permissions for authenticated role are derived from anonymous role
        /// when the authenticated role is not defined, but anonymous role is defined.
        /// </summary>
        [DataRow(new string[] { "col1", "col2", "col3" }, new string[] { "col4" },
            new string[] { "col2", "col3" }, true, DisplayName = "fields in include check")]
        [DataRow(new string[] { "col2", "col4" }, new string[] { "col1", "col3" },
            new string[] { "col1", "col4" }, false, DisplayName = "fields in exclude check")]
        [DataRow(new string[] { "col1" }, new string[] { "col2" },
            new string[] { "col2" }, false, DisplayName = "fields in include/exclude mix check")]
        [DataTestMethod]
        public void TestAuthenticatedRoleForColumnPermissionsWhenAnonymousRoleIsDefined(
            string[] includeCols,
            string[] excludeCols,
            string[] columnsToCheck,
            bool expected)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: AuthorizationResolver.ROLE_ANONYMOUS,
                operation: Operation.All,
                includedCols: new HashSet<string>(includeCols),
                excludedCols: new HashSet<string>(excludeCols));

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            foreach (Operation operation in PermissionOperation.ValidPermissionOperations)
            {
                Assert.AreEqual(expected, authZResolver.AreColumnsAllowedForOperation(
                    AuthorizationHelpers.TEST_ENTITY,
                    AuthorizationResolver.ROLE_AUTHENTICATED,
                    operation,
                    new List<string>(columnsToCheck)));
            }
        }

        /// <summary>
        /// Test to validate the AreColumnsAllowedForOperation method for case insensitivity of roleName.
        /// For eg. The role CREATOR is equivalent to creator, cReAtOR etc.
        /// </summary>
        /// <param name="operation">The operation configured on the entity.</param>
        /// <param name="configRole">The role configured on the entity.</param>
        /// <param name="columnsToInclude">Columns accessible for the given role and operation.</param>
        /// <param name="columnsToExclude">Columns inaccessible for the given role and operation.</param>
        /// <param name="roleName">The roleName to be tested, differs in casing with configRole.</param>
        /// <param name="columnsToCheck">Columns to be checked for access.</param>
        /// <param name="expected">Expected booolean result for the relevant method call.</param>
        [DataTestMethod]
        [DataRow(Operation.All, "Writer", new string[] { "col1", "col2" }, new string[] { "col3" }, "WRITER",
            new string[] { "col1", "col2" }, true, DisplayName = "Case insensitive role writer")]
        [DataRow(Operation.Read, "Reader", new string[] { "col1", "col3", "col4" }, new string[] { "col3" }, "reADeR",
            new string[] { "col1", "col3" }, false, DisplayName = "Case insensitive role reader")]
        [DataRow(Operation.Create, "Creator", new string[] { "col1", "col2" }, new string[] { "col3", "col4" }, "CREator",
            new string[] { "col1", "col2" }, true, DisplayName = "Case insensitive role creator")]
        public void AreColumnsAllowedForOperationWithRoleWithDifferentCasing(
            Operation operation,
            string configRole,
            string[] columnsToInclude,
            string[] columnsToExclude,
            string roleName,
            string[] columnsToCheck,
            bool expected)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: configRole,
                operation: operation,
                includedCols: new(columnsToInclude),
                excludedCols: new(columnsToExclude)
                );
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            List<Operation> operations = AuthorizationResolver.GetAllOperations(operation).ToList();

            foreach (Operation testOperation in operations)
            {
                // Assert that the expected result and the returned result are equal.
                Assert.AreEqual(expected,
                    authZResolver.AreColumnsAllowedForOperation(
                        AuthorizationHelpers.TEST_ENTITY,
                        roleName,
                        testOperation,
                        new List<string>(columnsToCheck)));
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
                TEST_OPERATION,
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

            string parsedPolicy = authZResolver.TryProcessDBPolicy(TEST_ENTITY, TEST_ROLE, TEST_OPERATION, context.Object);
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
                TEST_OPERATION,
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
                authZResolver.TryProcessDBPolicy(TEST_ENTITY, TEST_ROLE, TEST_OPERATION, context.Object);
            }
            catch (DataApiBuilderException ex)
            {
                Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
                Assert.AreEqual("User does not possess all the claims required to perform this operation.", ex.Message);
            }
        }

        /// <summary>
        /// Test to validate that duplicate claims throws an exception for everything except roles
        /// duplicate role claims are ignored, so just checks policy is parsed as expected in this case 
        /// </summary>
        /// <param name="exceptionExpected"> Whether we expect an exception (403 forbidden) to be thrown while parsing policy </param>
        /// <param name="claims"> Parameter list of claim types/keys to add to the claims dictionary that can be accessed with @claims </param>
        [DataTestMethod]
        [DataRow(true, ClaimTypes.Role, "username", "guid", "username",
            DisplayName = "duplicate claim expect exception")]
        [DataRow(false, ClaimTypes.Role, "username", "guid", ClaimTypes.Role,
            DisplayName = "duplicate role claim does not expect exception")]
        [DataRow(true, ClaimTypes.Role, ClaimTypes.Role, "username", "username",
            DisplayName = "duplicate claim expect exception ignoring role")]
        public void ParsePolicyWithDuplicateUserClaims(bool exceptionExpected, params string[] claimTypes)
        {
            string policy = $"@claims.guid eq 1";
            string defaultClaimValue = "unimportant";
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                TEST_ROLE,
                TEST_OPERATION,
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
                    authZResolver.TryProcessDBPolicy(TEST_ENTITY, TEST_ROLE, TEST_OPERATION, context.Object);
                    Assert.Fail();
                }
                catch (DataApiBuilderException ex)
                {
                    Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
                    Assert.AreEqual("Duplicate claims are not allowed within a request.", ex.Message);
                }
            }
            else
            {
                // If the role claim was the only duplicate, simply verify policy parsed as expected
                string expectedPolicy = $"('{defaultClaimValue}') eq 1";
                string parsedPolicy = authZResolver.TryProcessDBPolicy(TEST_ENTITY, TEST_ROLE, TEST_OPERATION, context.Object);
                Assert.AreEqual(expected: expectedPolicy, actual: parsedPolicy);
            }
        }

        // Indirectly tests the AuthorizationResolver private method:
        // GetDBPolicyForRequest(string entityName, string roleName, string operation)
        // by calling public method TryProcessDBPolicy(TEST_ENTITY, clientRole, requestOperation, context.Object)
        // The result of executing that method will determine whether execution behaves as expected.
        // When string.Empty is returned,
        // then no policy is found for the provided entity, role, and operation combination, therefore,
        // no predicates need to be added to the database query generated for the request.
        // When a value is returned as a result, the execution behaved as expected.
        [DataTestMethod]
        [DataRow("anonymous", "anonymous", Operation.Read, Operation.Read, "id eq 1", true,
            DisplayName = "Fetch Policy for existing system role - anonymous")]
        [DataRow("authenticated", "authenticated", Operation.Update, Operation.Update, "id eq 1", true,
            DisplayName = "Fetch Policy for existing system role - authenticated")]
        [DataRow("anonymous", "anonymous", Operation.Read, Operation.Read, null, false,
            DisplayName = "Fetch Policy for existing role, no policy object defined in config.")]
        [DataRow("anonymous", "authenticated", Operation.Read, Operation.Read, "id eq 1", false,
            DisplayName = "Fetch Policy for non-configured role")]
        [DataRow("anonymous", "anonymous", Operation.Read, Operation.Create, "id eq 1", false,
            DisplayName = "Fetch Policy for non-configured operation")]
        public void GetDBPolicyTest(
            string clientRole,
            string configuredRole,
            Operation requestOperation,
            Operation configuredOperation,
            string policy,
            bool expectPolicy)
        {
            RuntimeConfig runtimeConfig = InitRuntimeConfig(
                TEST_ENTITY,
                configuredRole,
                configuredOperation,
                databasePolicy: policy
                );

            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            Mock<HttpContext> context = new();

            // Add identity object to the Mock context object.
            ClaimsIdentity identity = new(TEST_AUTHENTICATION_TYPE, TEST_CLAIMTYPE_NAME, TEST_ROLE_TYPE);
            identity.AddClaim(new Claim("user_email", "xyz@microsoft.com", ClaimValueTypes.String));
            ClaimsPrincipal principal = new(identity);
            context.Setup(x => x.User).Returns(principal);
            context.Setup(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns(clientRole);

            string parsedPolicy = authZResolver.TryProcessDBPolicy(TEST_ENTITY, clientRole, requestOperation, context.Object);
            string errorMessage = "TryProcessDBPolicy returned unexpected value.";
            if (expectPolicy)
            {
                Assert.AreEqual(actual: parsedPolicy, expected: policy, message: errorMessage);
            }
            else
            {
                Assert.AreEqual(actual: parsedPolicy, expected: string.Empty, message: errorMessage);
            }
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Creates code-first in-memory RuntimeConfig.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="roleName">Role permitted to access entity.</param>
        /// <param name="operation">Operation to allow on role</param>
        /// <param name="includedCols">Allowed columns to access for operation defined on role.</param>
        /// <param name="excludedCols">Excluded columns to access for operation defined on role.</param>
        /// <param name="requestPolicy">Request authorization policy. (Support TBD)</param>
        /// <param name="databasePolicy">Database authorization policy.</param>
        /// <returns>Mocked RuntimeConfig containing metadata provided in method arguments.</returns>
        public static RuntimeConfig InitRuntimeConfig(
            string entityName = "SampleEntity",
            string roleName = "Reader",
            Operation operation = Operation.Create,
            HashSet<string>? includedCols = null,
            HashSet<string>? excludedCols = null,
            string? requestPolicy = null,
            string? databasePolicy = null
            )
        {
            Field fieldsForRole = new(
                include: includedCols,
                exclude: excludedCols);

            Policy? policy = null;

            if (databasePolicy is not null || requestPolicy is not null)
            {
                policy = new(
                    request: requestPolicy,
                    database: databasePolicy);
            }

            PermissionOperation operationForRole = new(
                Name: operation,
                Fields: fieldsForRole,
                Policy: policy);

            PermissionSetting permissionForEntity = new(
                role: roleName,
                operations: new object[] { JsonSerializer.SerializeToElement(operationForRole) });

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

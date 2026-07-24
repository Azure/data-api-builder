// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <see cref="McpAuthorizationHelper"/>. Uses a mocked
    /// <see cref="IAuthorizationResolver"/>; no database required.
    /// </summary>
    [TestClass]
    public class McpAuthorizationHelperTests
    {
        [TestMethod]
        public void ValidateRoleContext_NullHttpContext_ReturnsFalse()
        {
            Mock<IAuthorizationResolver> resolver = new();

            bool ok = McpAuthorizationHelper.ValidateRoleContext(null, resolver.Object, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "valid role context");
        }

        [TestMethod]
        public void ValidateRoleContext_InvalidRoleContext_ReturnsFalse()
        {
            Mock<IAuthorizationResolver> resolver = new();
            resolver.Setup(r => r.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(false);

            bool ok = McpAuthorizationHelper.ValidateRoleContext(new DefaultHttpContext(), resolver.Object, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(error, "valid role context");
        }

        [TestMethod]
        public void ValidateRoleContext_ValidRoleContext_ReturnsTrue()
        {
            Mock<IAuthorizationResolver> resolver = new();
            resolver.Setup(r => r.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);

            bool ok = McpAuthorizationHelper.ValidateRoleContext(new DefaultHttpContext(), resolver.Object, out string error);

            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryResolveAuthorizedRole_MissingRoleHeader_ReturnsFalse()
        {
            Mock<IAuthorizationResolver> resolver = new();

            bool ok = McpAuthorizationHelper.TryResolveAuthorizedRole(
                new DefaultHttpContext(),
                resolver.Object,
                "Book",
                EntityActionOperation.Read,
                out string? effectiveRole,
                out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(effectiveRole);
            StringAssert.Contains(error, "role header");
        }

        [TestMethod]
        public void TryResolveAuthorizedRole_AllowedRole_ReturnsTrue()
        {
            Mock<IAuthorizationResolver> resolver = new();
            resolver.Setup(r => r.AreRoleAndOperationDefinedForEntity("Book", "reader", EntityActionOperation.Read))
                    .Returns(true);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "reader";

            bool ok = McpAuthorizationHelper.TryResolveAuthorizedRole(
                httpContext,
                resolver.Object,
                "Book",
                EntityActionOperation.Read,
                out string? effectiveRole,
                out string error);

            Assert.IsTrue(ok);
            Assert.AreEqual("reader", effectiveRole);
            Assert.AreEqual(string.Empty, error);
        }

        [TestMethod]
        public void TryResolveAuthorizedRole_NoAllowedRole_ReturnsFalse()
        {
            Mock<IAuthorizationResolver> resolver = new();
            resolver.Setup(r => r.AreRoleAndOperationDefinedForEntity(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                    .Returns(false);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "reader,writer";

            bool ok = McpAuthorizationHelper.TryResolveAuthorizedRole(
                httpContext,
                resolver.Object,
                "Book",
                EntityActionOperation.Read,
                out string? effectiveRole,
                out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(effectiveRole);
            StringAssert.Contains(error, "permission");
        }
    }
}

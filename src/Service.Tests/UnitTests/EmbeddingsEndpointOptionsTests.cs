// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="EmbeddingsEndpointOptions"/> role-resolution logic
    /// (<c>GetEffectiveRoles</c> / <c>IsRoleAllowed</c>). Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class EmbeddingsEndpointOptionsTests
    {
        [TestMethod]
        public void GetEffectiveRoles_ExplicitRoles_AreReturned()
        {
            EmbeddingsEndpointOptions options = new(enabled: true, roles: new[] { "reader", "writer" });

            CollectionAssert.AreEqual(new[] { "reader", "writer" }, options.GetEffectiveRoles(isDevelopmentMode: false));
            CollectionAssert.AreEqual(new[] { "reader", "writer" }, options.GetEffectiveRoles(isDevelopmentMode: true));
        }

        [TestMethod]
        public void GetEffectiveRoles_NoRoles_DevelopmentMode_ReturnsAnonymous()
        {
            EmbeddingsEndpointOptions options = new(enabled: true);

            CollectionAssert.AreEqual(EmbeddingsEndpointOptions.DEFAULT_ROLES_DEVELOPMENT, options.GetEffectiveRoles(isDevelopmentMode: true));
        }

        [TestMethod]
        public void GetEffectiveRoles_NoRoles_ProductionMode_ReturnsAuthenticated()
        {
            EmbeddingsEndpointOptions options = new(enabled: true);

            CollectionAssert.AreEqual(EmbeddingsEndpointOptions.DEFAULT_ROLES, options.GetEffectiveRoles(isDevelopmentMode: false));
        }

        [TestMethod]
        public void GetEffectiveRoles_EmptyRolesArray_FallsBackToDefaults()
        {
            EmbeddingsEndpointOptions options = new(enabled: true, roles: new string[0]);

            CollectionAssert.AreEqual(EmbeddingsEndpointOptions.DEFAULT_ROLES, options.GetEffectiveRoles(isDevelopmentMode: false));
        }

        [DataTestMethod]
        [DataRow("authenticated", false, true, DisplayName = "authenticated allowed in production")]
        [DataRow("AUTHENTICATED", false, true, DisplayName = "role check is case-insensitive")]
        [DataRow("anonymous", true, true, DisplayName = "anonymous allowed in development")]
        [DataRow("admin", false, false, DisplayName = "unknown role denied in production")]
        public void IsRoleAllowed_DefaultRoles_MatchesEnvironment(string role, bool isDevelopment, bool expected)
        {
            EmbeddingsEndpointOptions options = new(enabled: true);

            Assert.AreEqual(expected, options.IsRoleAllowed(role, isDevelopment));
        }

        [TestMethod]
        public void IsRoleAllowed_ExplicitRoles_ChecksConfiguredList()
        {
            EmbeddingsEndpointOptions options = new(enabled: true, roles: new[] { "reader" });

            Assert.IsTrue(options.IsRoleAllowed("reader", isDevelopmentMode: false));
            Assert.IsTrue(options.IsRoleAllowed("READER", isDevelopmentMode: false));
            Assert.IsFalse(options.IsRoleAllowed("authenticated", isDevelopmentMode: false));
        }
    }
}

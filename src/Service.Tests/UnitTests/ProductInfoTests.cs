// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="ProductInfo"/> version and user-agent helpers.
    /// Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class ProductInfoTests
    {
        [TestMethod]
        public void GetProductVersion_ReturnsMajorMinorPatch()
        {
            string version = ProductInfo.GetProductVersion();

            Assert.IsFalse(string.IsNullOrWhiteSpace(version));
            Assert.IsTrue(version.Split('.').Length >= 3, $"Expected Major.Minor.Patch, got '{version}'.");
        }

        [TestMethod]
        public void GetProductVersion_WithCommitHash_ReturnsNonEmpty()
        {
            string version = ProductInfo.GetProductVersion(includeCommitHash: true);

            Assert.IsFalse(string.IsNullOrWhiteSpace(version));
        }

        [TestMethod]
        public void GetDataApiBuilderUserAgent_WhenEnvNotSet_ReturnsDefault()
        {
            string? original = Environment.GetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV);
            try
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, null);

                string userAgent = ProductInfo.GetDataApiBuilderUserAgent();

                Assert.AreEqual(ProductInfo.DAB_USER_AGENT, userAgent);
                StringAssert.StartsWith(userAgent, "dab_oss_");
            }
            finally
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, original);
            }
        }

        [TestMethod]
        public void GetDataApiBuilderUserAgent_WhenEnvSet_ReturnsEnvValue()
        {
            string? original = Environment.GetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV);
            try
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, "custom-agent");

                Assert.AreEqual("custom-agent", ProductInfo.GetDataApiBuilderUserAgent());
            }
            finally
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, original);
            }
        }
    }
}

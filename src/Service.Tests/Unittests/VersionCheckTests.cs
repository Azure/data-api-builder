// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cli.Tests
{
    [TestClass]
    public class VersionCheckTests
    {
        [TestMethod]
        public void GetVersions_LatestVersionNotNull()
        {
            VersionChecker.GetVersions(out string latestVersion, out string _);
            Assert.IsNotNull(latestVersion, "Latest version should not be null.");
        }

        [TestMethod]
        public void GetVersions_CurrentVersionNotNull()
        {
            VersionChecker.GetVersions(out string _, out string currentVersion);
            Assert.IsNotNull(currentVersion, "Current version should not be null.");
        }
    }
}

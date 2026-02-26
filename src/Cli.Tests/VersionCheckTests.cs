// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core;

namespace Cli.Tests;

[TestClass]
public class VersionCheckTests
{
    [TestMethod]
    public void GetVersions_LatestVersionNotNull()
    {
        VersionChecker.IsCurrentVersion(out string? nugetVersion, out string? _);
        Assert.IsNotNull(nugetVersion, "Nuget version should not be null.");
    }

    [TestMethod]
    public void GetVersions_CurrentVersionNotNull()
    {
        VersionChecker.IsCurrentVersion(out string? _, out string? localVersion);
        Assert.IsNotNull(localVersion, "Local version should not be null.");
    }

    [TestMethod]
    public void GetVersions_IsNotInNuGet()
    {
        bool result = VersionChecker.IsCurrentVersion(out string? nugetVersion, out string? localVersion);
        Assert.IsFalse(result, $"Should not be in NuGet. {localVersion} -> {nugetVersion}");
    }
}

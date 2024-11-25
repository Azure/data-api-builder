// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Contains test involving environment variables.
/// </summary>
[TestClass]
public class EnvironmentTests
{
    /// <summary>
    /// Tests the behavior of the <c>Main</c> method when the <c>ASPNETCORE_URLS</c> environment variable is set to an invalid value.
    /// </summary>
    /// <remarks>
    /// This test sets the <c>ASPNETCORE_URLS</c> environment variable to an invalid value, invokes the <c>Main</c> method,
    /// and verifies that the application exits with an error code of -1. Additionally, it checks if the error message
    /// contains the name of the invalid environment variable.
    /// </remarks>
    [TestMethod]
    public void Main_WhenAspNetCoreUrlsInvalid_ShouldExitWithError()
    {
        const string ASPNETCORE_URLS_NAME = "ASPNETCORE_URLS";
        const string ASPNETCORE_URLS_INVALID_VALUE = nameof(Main_WhenAspNetCoreUrlsInvalid_ShouldExitWithError);
        string originalEnvValue = Environment.GetEnvironmentVariable(ASPNETCORE_URLS_NAME);

        // Arrange
        Environment.SetEnvironmentVariable(ASPNETCORE_URLS_NAME, ASPNETCORE_URLS_INVALID_VALUE);
        using StringWriter consoleOutput = new();
        Console.SetError(consoleOutput);

        // Act
        Program.Main(Array.Empty<string>());

        // Assert
        Assert.AreEqual(-1, Environment.ExitCode);
        StringAssert.Contains(consoleOutput.ToString(), ASPNETCORE_URLS_NAME, StringComparison.Ordinal);

        // Cleanup
        Environment.SetEnvironmentVariable(ASPNETCORE_URLS_NAME, originalEnvValue);
    }
}

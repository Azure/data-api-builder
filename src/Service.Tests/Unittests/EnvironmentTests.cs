// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Contains test involving environment variables.
/// </summary>
[TestClass]
public class EnvironmentTests
{
    private const string ASPNETCORE_URLS_NAME = "ASPNETCORE_URLS";

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

    /// <summary>
    /// Tests the `ValidateAspNetCoreUrls` method with various inputs to ensure it correctly validates URLs.
    /// </summary>
    [DataTestMethod]
    [DataRow(null, true, DisplayName = "null input")]
    [DataRow("", true, DisplayName = "empty string")]
    [DataRow(" ", false, DisplayName = "whitespace only")]
    [DataRow("http://localhost", true, DisplayName = "valid URL")]
    [DataRow("https://localhost", true, DisplayName = "valid secure URL")]
    [DataRow("http://127.0.0.1:5000", true, DisplayName = "valid IP URL")]
    [DataRow("https://127.0.0.1:5001", true, DisplayName = "valid secure IP URL")]
    [DataRow("http://[::1]:80", true, DisplayName = "valid IPv6 URL")]
    [DataRow("https://[::1]:443", true, DisplayName = "valid secure IPv6 URL")]
    [DataRow("http://+:80/", true, DisplayName = "wildcard '+' host")]
    [DataRow("https://+:443/", true, DisplayName = "secure wildcard '+' host")]
    [DataRow("http://*:80/", true, DisplayName = "wildcard '*' host")]
    [DataRow("https://*:443/", true, DisplayName = "secure wildcard '*' host")]
    [DataRow("http://localhost:80/;https://localhost:443/", true, DisplayName = "semicolon-separated URLs")]
    [DataRow("http://localhost:80/ https://localhost:443/", true, DisplayName = "space-separated URLs")]
    [DataRow("http://localhost:80/,https://localhost:443/", true, DisplayName = "comma-separated URLs")]
    [DataRow("ftp://localhost:21", false, DisplayName = "invalid scheme (ftp)")]
    [DataRow("localhost:80", false, DisplayName = "missing scheme")]
    [DataRow("http://", false, DisplayName = "incomplete URL")]
    [DataRow("http://unix:/var/run/app.sock", true, DisplayName = "unix socket (Linux)")]
    [DataRow("https://unix:/var/run/app.sock", true, DisplayName = "secure unix socket (Linux)")]
    [DataRow("http://unix:var/run/app.sock", false, DisplayName = "unix socket missing slash")]
    [DataRow("http://unix:", false, DisplayName = "unix socket missing path")]
    [DataRow("http://unix:/var/run/app.sock;https://unix:/var/run/app2.sock", true, DisplayName = "multiple unix sockets (Linux)")]
    [DataRow("http://localhost:80/;ftp://localhost:21", false, DisplayName = "mixed valid/invalid schemes")]
    [DataRow("  http://localhost:80/  ", true, DisplayName = "trimmed whitespace")]
    public void ValidateAspNetCoreUrls_Test(string input, bool expected)
    {
        // Arrange
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            input is not null &&
            (input.StartsWith("http://unix:", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://unix:", StringComparison.OrdinalIgnoreCase)))
        {
            expected = false;
        }

        string originalEnvValue = Environment.GetEnvironmentVariable(ASPNETCORE_URLS_NAME);
        Environment.SetEnvironmentVariable(ASPNETCORE_URLS_NAME, input);

        // Act
        Assert.AreEqual(expected, Program.ValidateAspNetCoreUrls());

        // Cleanup
        Environment.SetEnvironmentVariable(ASPNETCORE_URLS_NAME, originalEnvValue);
    }
}

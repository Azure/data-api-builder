// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Tests for CustomLoggerProvider and CustomConsoleLogger, verifying
/// that log level labels use ASP.NET Core abbreviated format.
/// </summary>
[TestClass]
public class CustomLoggerTests
{
    /// <summary>
    /// Validates that each enabled log level produces the correct abbreviated label
    /// matching ASP.NET Core's default console formatter convention.
    /// Trace and Debug are below the logger's minimum level and produce no output.
    /// </summary>
    [DataTestMethod]
    [DataRow(LogLevel.Information, "info:")]
    [DataRow(LogLevel.Warning, "warn:")]
    public void LogOutput_UsesAbbreviatedLogLevelLabels(LogLevel logLevel, string expectedPrefix)
    {
        CustomLoggerProvider provider = new();
        ILogger logger = provider.CreateLogger("TestCategory");

        TextWriter originalOut = Console.Out;
        try
        {
            StringWriter writer = new();
            Console.SetOut(writer);

            logger.Log(logLevel, "test message");

            string output = writer.ToString();
            Assert.IsTrue(
                output.StartsWith(expectedPrefix),
                $"Expected output to start with '{expectedPrefix}' but got: '{output}'");
            Assert.IsTrue(
                output.Contains("test message"),
                $"Expected output to contain 'test message' but got: '{output}'");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    /// Validates that each log level error and above produces the correct abbreviated
    /// label matching ASP.NET Core's default console formatter convention.
    /// Error and Critical logs should go to the stderr stream.
    /// </summary>
    [DataTestMethod]
    [DataRow(LogLevel.Error, "fail:")]
    [DataRow(LogLevel.Critical, "crit:")]
    public void LogError_UsesAbbreviatedLogLevelLabels(LogLevel logLevel, string expectedPrefix)
    {
        CustomLoggerProvider provider = new();
        ILogger logger = provider.CreateLogger("TestCategory");

        TextWriter originalError = Console.Error;
        try
        {
            StringWriter writer = new();
            Console.SetError(writer);
            logger.Log(logLevel, "test message");

            string output = writer.ToString();
            Assert.IsTrue(
                output.StartsWith(expectedPrefix),
                $"Expected output to start with '{expectedPrefix}' but got: '{output}'");
            Assert.IsTrue(
                output.Contains("test message"),
                $"Expected output to contain 'test message' but got: '{output}'");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}

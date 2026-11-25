// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="BootstrapLogger"/>, the centralized logger used for
    /// diagnostics emitted before (or outside of) the dependency injection provided
    /// logging pipeline. Every entry must begin with an ISO 8601 UTC timestamp with
    /// millisecond precision, and MCP stdio hosts must be able to route all output
    /// to stderr so stdout stays reserved for JSON-RPC.
    /// </summary>
    [TestClass]
    public class BootstrapLoggerTests
    {
        private const string TIMESTAMP_PATTERN = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z ";

        [TestInitialize]
        [TestCleanup]
        public void ResetStandardErrorRouting()
        {
            BootstrapLogger.WriteAllOutputToStandardError = false;
        }

        /// <summary>
        /// Redirects Console.Out and Console.Error around <paramref name="action"/>
        /// and returns whatever was written to each.
        /// </summary>
        private static (string Stdout, string Stderr) CaptureConsole(Action action)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            StringWriter stdout = new();
            StringWriter stderr = new();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                action();
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            return (stdout.ToString(), stderr.ToString());
        }

        [DataTestMethod]
        [DataRow(LogLevel.Information, "info", false, DisplayName = "Information is written to stdout")]
        [DataRow(LogLevel.Warning, "warn", false, DisplayName = "Warning is written to stdout")]
        [DataRow(LogLevel.Error, "fail", true, DisplayName = "Error is written to stderr")]
        [DataRow(LogLevel.Critical, "crit", true, DisplayName = "Critical is written to stderr")]
        public void Log_PrefixesUtcTimestampAndAbbreviatedLevel(LogLevel logLevel, string expectedAbbreviation, bool expectStderr)
        {
            const string message = "bootstrap diagnostic message";

            (string stdout, string stderr) = CaptureConsole(
                () => BootstrapLogger.Instance.Log(logLevel, default, message, null, (state, _) => state));

            string actual = expectStderr ? stderr : stdout;
            string other = expectStderr ? stdout : stderr;

            Assert.IsTrue(
                Regex.IsMatch(actual, TIMESTAMP_PATTERN + Regex.Escape($"{expectedAbbreviation}: {message}")),
                $"Expected an ISO 8601 UTC timestamp followed by '{expectedAbbreviation}: {message}' but got: '{actual}'");
            Assert.AreEqual(string.Empty, other,
                $"Did not expect output on the other stream but got: '{other}'");
        }

        [TestMethod]
        public void Log_WhenWriteAllOutputToStandardError_RoutesInformationToStandardError()
        {
            BootstrapLogger.WriteAllOutputToStandardError = true;

            (string stdout, string stderr) = CaptureConsole(
                () => BootstrapLogger.Instance.LogInformation("mcp safe message"));

            Assert.AreEqual(string.Empty, stdout, $"Expected stdout to stay clean but got: '{stdout}'");
            Assert.IsTrue(
                Regex.IsMatch(stderr, TIMESTAMP_PATTERN + "info: mcp safe message"),
                $"Expected timestamped entry on stderr but got: '{stderr}'");
        }

        [TestMethod]
        public void Log_WhenLogLevelNone_WritesNothing()
        {
            (string stdout, string stderr) = CaptureConsole(
                () => BootstrapLogger.Instance.Log(LogLevel.None, default, "suppressed", null, (state, _) => state));

            Assert.AreEqual(string.Empty, stdout);
            Assert.AreEqual(string.Empty, stderr);
        }
    }
}

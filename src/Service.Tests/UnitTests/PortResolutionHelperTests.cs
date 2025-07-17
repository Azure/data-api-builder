// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Service.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Tests for the <see cref="PortResolutionHelper"/> class, which resolves the internal port used by the application
    /// </summary>
    [TestClass]
    public class PortResolutionHelperTests
    {
        /// <summary>
        /// Tests the <see cref="PortResolutionHelper.ResolveInternalPort"/> method to ensure it resolves the correct
        /// port.
        /// </summary>
        /// <remarks>This test method sets the "ASPNETCORE_URLS" environment variable to various test
        /// cases and verifies that the <see cref="PortResolutionHelper.ResolveInternalPort"/> method returns the
        /// expected port. It handles different URL formats and edge cases, including null or invalid inputs.</remarks>
        /// <param name="aspnetcoreUrls">A string representing the ASP.NET Core URLs to be tested.</param>
        /// <param name="expectedPort">The expected port number that should be resolved.</param>
        [DataTestMethod]
        [DataRow("http://localhost:5000", 5000)]
        [DataRow("https://localhost:443", 443)]
        [DataRow("http://+:1234", 1234)]
        [DataRow("https://*:8443", 8443)]
        [DataRow("http://localhost:5000;https://localhost:443", 5000)]
        [DataRow("https://localhost:443;http://localhost:5000", 5000)]
        [DataRow("http://localhost:5000,https://localhost:443", 5000)]
        [DataRow(null, 5000)]
        [DataRow("", 5000)]
        [DataRow("http://localhost", 80)]
        [DataRow("https://localhost", 443)]
        [DataRow("http://[::1]:5000", 5000)]
        [DataRow("http://localhost;https://localhost:8443", 80)]
        [DataRow("https://localhost:8443;https://localhost:9443", 8443)]
        [DataRow("invalid;http://localhost:5000", 5000)]
        [DataRow("http://localhost:5000;invalid", 5000)]
        [DataRow("http://+:", 5000)]
        [DataRow("https://localhost:5001;http://localhost:5000", 5000)]
        [DataRow("https://localhost:5001;https://localhost:5002", 5001)]
        public void ResolveInternalPortResolvesCorrectPortPositiveTest(string aspnetcoreUrls, int expectedPort)
        {
            TestPortResolution(aspnetcoreUrls, null, expectedPort);
        }

        /// <summary>
        /// Tests that the <see cref="PortResolutionHelper.ResolveInternalPort"/> method uses the  "DEFAULT_PORT"
        /// environment variable when the "ASPNETCORE_URLS" environment variable is not set.
        /// </summary>
        /// <remarks>This test sets the "DEFAULT_PORT" environment variable to "4321" and verifies that 
        /// <see cref="PortResolutionHelper.ResolveInternalPort"/> returns this value. It ensures that  the method
        /// correctly defaults to using "DEFAULT_PORT" when "ASPNETCORE_URLS" is null.</remarks>
        [TestMethod]
        public void ResolveInternalPortUsesDefaultPortEnvVarTest()
        {
            TestPortResolution(null, "4321", 4321);
        }

        /// <summary>
        /// Tests that the <see cref="PortResolutionHelper.ResolveInternalPort"/> method uses the default port when the
        /// environment variable <c>ASPNETCORE_URLS</c> is set to invalid values.
        /// </summary>
        /// <remarks>This test sets the <c>ASPNETCORE_URLS</c> environment variable to invalid URLs and
        /// the <c>DEFAULT_PORT</c> environment variable to a valid port number. It verifies that <see
        /// cref="PortResolutionHelper.ResolveInternalPort"/> correctly falls back to using the default port specified
        /// by <c>DEFAULT_PORT</c>.</remarks>
        [TestMethod]
        public void ResolveInternalPortUsesDefaultPortWhenUrlsAreInvalidTest()
        {
            TestPortResolution("invalid-url;another-invalid", "4321", 4321);
        }

        /// <summary>
        /// Tests that the <see cref="PortResolutionHelper.ResolveInternalPort"/> method falls back to the default port
        /// when the <c>DEFAULT_PORT</c> environment variable is set to a non-numeric value.
        /// </summary>
        /// <remarks>This test sets the <c>DEFAULT_PORT</c> environment variable to an invalid value and
        /// verifies that <see cref="PortResolutionHelper.ResolveInternalPort"/> correctly falls back to using
        /// the default port of 5000 when the <c>DEFAULT_PORT</c> cannot be parsed as a valid integer.</remarks>
        [TestMethod]
        public void ResolveInternalPortFallsBackToDefaultWhenDefaultPortIsInvalidTest()
        {
            TestPortResolution(null, "abc", 5000);
        }

        /// <summary>
        /// Negative tests for the <see cref="PortResolutionHelper.ResolveInternalPort"/> method.
        /// </summary>
        /// <param name="aspnetcoreUrls">A string representing the ASP.NET Core URLs to be tested.</param>
        /// <param name="expectedPort">The expected port number that should be resolved.</param>
        [DataTestMethod]
        [DataRow("http://localhost:5000 https://localhost:443", 5000)] // space invalid, falls back to default
        [DataRow("http://localhost:5000|https://localhost:443", 5000)] // invalid delimiter, falls back to default
        [DataRow("localhost:5000", 5000)] // missing scheme: fallback to default
        [DataRow("http://:", 5000)] // incomplete URL: fallback to default
        [DataRow("ftp://localhost:21", 5000)] // unsupported scheme: fallback to default
        [DataRow("http://unix:/var/run/app.sock", 80)] // unix socket: defaults to 80 (no port specified)
        [DataRow("http://unix:var/run/app.sock", 5000)] // malformed unix socket: fallback to default
        [DataRow("http://unix:", 80)] // incomplete unix socket: defaults to 80
        public void ResolveInternalPortResolvesCorrectPortNegativeTest(string aspnetcoreUrls, int expectedPort)
        {
            TestPortResolution(aspnetcoreUrls, null, expectedPort);
        }

        /// <summary>
        /// Helper method to test port resolution with environment variables.
        /// </summary>
        /// <param name="aspnetcoreUrls">The ASPNETCORE_URLS environment variable value to set.</param>
        /// <param name="defaultPort">The DEFAULT_PORT environment variable value to set.</param>
        /// <param name="expectedPort">The expected port number that should be resolved.</param>
        private static void TestPortResolution(string aspnetcoreUrls, string defaultPort, int expectedPort)
        {
            string originalUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            string originalDefaultPort = Environment.GetEnvironmentVariable("DEFAULT_PORT");
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", aspnetcoreUrls);
            Environment.SetEnvironmentVariable("DEFAULT_PORT", defaultPort);
            try
            {
                int port = PortResolutionHelper.ResolveInternalPort();
                Assert.AreEqual(expectedPort, port);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", originalUrls);
                Environment.SetEnvironmentVariable("DEFAULT_PORT", originalDefaultPort);
            }
        }
    }
}

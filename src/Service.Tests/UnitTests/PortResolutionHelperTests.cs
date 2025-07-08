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
        public void ResolveInternalPort_ResolvesCorrectPort_Positive(string aspnetcoreUrls, int expectedPort)
        {
            string originalUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            string originalDefaultPort = Environment.GetEnvironmentVariable("DEFAULT_PORT");
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", aspnetcoreUrls);
            Environment.SetEnvironmentVariable("DEFAULT_PORT", null);
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

        /// <summary>
        /// Tests that the <see cref="PortResolutionHelper.ResolveInternalPort"/> method uses the  "DEFAULT_PORT"
        /// environment variable when the "ASPNETCORE_URLS" environment variable is not set.
        /// </summary>
        /// <remarks>This test sets the "DEFAULT_PORT" environment variable to "4321" and verifies that 
        /// <see cref="PortResolutionHelper.ResolveInternalPort"/> returns this value. It ensures that  the method
        /// correctly defaults to using "DEFAULT_PORT" when "ASPNETCORE_URLS" is null.</remarks>
        [TestMethod]
        public void ResolveInternalPort_UsesDefaultPortEnvVar()
        {
            string originalUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            string originalDefaultPort = Environment.GetEnvironmentVariable("DEFAULT_PORT");
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
            Environment.SetEnvironmentVariable("DEFAULT_PORT", "4321");
            try
            {
                int port = PortResolutionHelper.ResolveInternalPort();
                Assert.AreEqual(4321, port);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", originalUrls);
                Environment.SetEnvironmentVariable("DEFAULT_PORT", originalDefaultPort);
            }
        }

        // Negative scenarios: invalid/unsupported formats, fallback behavior
        [DataTestMethod]
        [DataRow("http://localhost:5000 https://localhost:443", 443)] // space delimiter: fallback to first HTTPS port
        [DataRow("http://localhost:5000|https://localhost:443", 443)] // pipe delimiter: fallback to first HTTPS port
        [DataRow("localhost:5000", 5000)] // missing scheme: fallback to default
        [DataRow("http://", 5000)] // incomplete URL: fallback to default
        [DataRow("ftp://localhost:21", 5000)] // unsupported scheme: fallback to default
        [DataRow("http://unix:/var/run/app.sock", 80)] // unix socket: defaults to 80 (no port specified)
        [DataRow("http://unix:var/run/app.sock", 5000)] // malformed unix socket: fallback to default
        [DataRow("http://unix:", 80)] // incomplete unix socket: defaults to 80
        public void ResolveInternalPort_ResolvesCorrectPort_Negative(string aspnetcoreUrls, int expectedPort)
        {
            string originalUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            string originalDefaultPort = Environment.GetEnvironmentVariable("DEFAULT_PORT");
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", aspnetcoreUrls);
            Environment.SetEnvironmentVariable("DEFAULT_PORT", null);
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

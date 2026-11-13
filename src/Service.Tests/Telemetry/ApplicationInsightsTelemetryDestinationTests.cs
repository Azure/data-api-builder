// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class ApplicationInsightsTelemetryDestinationTests
    {
        private const string KEY = "01234567-89ab-cdef-0123-456789abcdef";
        private const string CONNECTION_STRING = "InstrumentationKey=" + KEY + ";IngestionEndpoint=https://synthetic.invalid/";

        [DataTestMethod]
        [DataRow(CONNECTION_STRING, "https://synthetic.invalid/v2.1/track")]
        [DataRow(" instrumentationkey = " + KEY + " ; ingestionendpoint = https://synthetic.invalid ; ", "https://synthetic.invalid/v2.1/track")]
        [DataRow("InstrumentationKey=" + KEY + ";EndpointSuffix=applicationinsights.azure.com", "https://dc.applicationinsights.azure.com/v2.1/track")]
        [DataRow("InstrumentationKey=" + KEY + ";EndpointSuffix=applicationinsights.azure.cn;Location=westus2", "https://westus2.dc.applicationinsights.azure.cn/v2.1/track")]
        [DataRow("InstrumentationKey=" + KEY + ";EndpointSuffix=applicationinsights.us", "https://dc.applicationinsights.us/v2.1/track")]
        [DataRow(CONNECTION_STRING + ";LiveEndpoint=https://live.synthetic.invalid/;ApplicationId=" + KEY + ";Authorization=ikey", "https://synthetic.invalid/v2.1/track")]
        [DataRow(CONNECTION_STRING + ";EndpointSuffix=applicationinsights.azure.com;Location=westus2", "https://synthetic.invalid/v2.1/track")]
        public void ValidPortalRoutingIsParsedWithoutRetainingUnusedSettings(string input, string trackEndpoint)
        {
            Assert.IsTrue(ApplicationInsightsTelemetryDestination.TryParse(input, out ApplicationInsightsTelemetryDestination? destination));
            Assert.AreEqual(Guid.Parse(KEY), destination.InstrumentationKey);
            Assert.AreEqual(new Uri(trackEndpoint), destination.TrackEndpoint);
            Assert.IsFalse(destination.ToString().Contains(KEY, StringComparison.Ordinal));
            Assert.IsFalse(destination.ToString().Contains(trackEndpoint, StringComparison.Ordinal));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow(KEY)]
        [DataRow("InstrumentationKey=" + KEY)]
        [DataRow("IngestionEndpoint=https://synthetic.invalid/")]
        [DataRow("InstrumentationKey=invalid;IngestionEndpoint=https://synthetic.invalid/")]
        [DataRow("InstrumentationKey=0123456789abcdef0123456789abcdef;IngestionEndpoint=https://synthetic.invalid/")]
        [DataRow("InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://synthetic.invalid/")]
        [DataRow(CONNECTION_STRING + ";instrumentationkey=" + KEY)]
        [DataRow(CONNECTION_STRING + ";IngestionEndpoint=https://synthetic.invalid/")]
        [DataRow(CONNECTION_STRING + ";;Authorization=ikey")]
        [DataRow(CONNECTION_STRING + ";ApiKey=secret")]
        [DataRow(CONNECTION_STRING + ";ClientSecret=secret")]
        [DataRow(CONNECTION_STRING + ";Authorization=Bearer token")]
        [DataRow(CONNECTION_STRING + ";AADAudience=https://monitor.azure.com/")]
        [DataRow(CONNECTION_STRING + ";ApplicationId=invalid")]
        [DataRow(CONNECTION_STRING + ";LiveEndpoint=http://localhost/")]
        [DataRow(CONNECTION_STRING + ";Location=westus2")]
        [DataRow("InstrumentationKey=" + KEY + ";EndpointSuffix=attacker.invalid")]
        [DataRow("InstrumentationKey=" + KEY + ";EndpointSuffix=applicationinsights.azure.com/path")]
        [DataRow("InstrumentationKey=" + KEY + ";EndpointSuffix=applicationinsights.azure.com;Location=west/us")]
        [DataRow(CONNECTION_STRING + "\r\n")]
        [DataRow(CONNECTION_STRING + ";Authorization=")]
        public void MalformedOrCredentialBearingRoutingIsRejected(string? input)
        {
            Assert.IsFalse(ApplicationInsightsTelemetryDestination.TryParse(input, out ApplicationInsightsTelemetryDestination? destination));
            Assert.IsNull(destination);
        }

        [DataTestMethod]
        [DataRow("http://localhost/")]
        [DataRow("http://127.0.0.1:4321/")]
        [DataRow("http://ingestion.invalid/")]
        [DataRow("ftp://ingestion.invalid/")]
        [DataRow("https://user:password@ingestion.invalid/")]
        [DataRow("https://ingestion.invalid/?token=private")]
        [DataRow("https://ingestion.invalid/?")]
        [DataRow("https://ingestion.invalid/#fragment")]
        [DataRow("https://ingestion.invalid/v2.1/track")]
        [DataRow("https://ingestion.invalid/a/..")]
        [DataRow("https://ingestion.invalid/a/../")]
        [DataRow("https://ingestion.invalid/%2e%2e/")]
        [DataRow("https://ingestion.invalid\\path")]
        [DataRow("https://ingestion.invalid//")]
        [DataRow("https://ingestion.invalid:0/")]
        public void EndpointMustBeAnExplicitHttpsOrigin(string endpoint)
        {
            Assert.IsFalse(ApplicationInsightsTelemetryDestination.TryParse("InstrumentationKey=" + KEY + ";IngestionEndpoint=" + endpoint, out _));
        }

        [TestMethod]
        public void ConnectionStringParsingIsBounded()
        {
            Assert.IsFalse(ApplicationInsightsTelemetryDestination.TryParse(new string('a', 4097), out _));
        }
    }
}

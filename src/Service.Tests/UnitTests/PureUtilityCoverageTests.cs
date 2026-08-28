// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using MetadataTypeConverter = Azure.DataApiBuilder.Core.Services.MetadataProviders.Converters.TypeConverter;
using Azure.DataApiBuilder.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class PureUtilityCoverageTests
    {
        [DataTestMethod]
        [DataRow("debug", LogLevel.Debug)]
        [DataRow("INFO", LogLevel.Information)]
        [DataRow("notice", LogLevel.Information)]
        [DataRow("warning", LogLevel.Warning)]
        [DataRow("error", LogLevel.Error)]
        [DataRow("critical", LogLevel.Critical)]
        [DataRow("alert", LogLevel.Critical)]
        [DataRow("emergency", LogLevel.Critical)]
        public void McpLogLevelConverter_RecognizedValuesMapToLogLevels(string value, LogLevel expected)
        {
            Assert.IsTrue(McpLogLevelConverter.TryConvertFromMcp(value, out LogLevel actual));
            Assert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow("unknown")]
        public void McpLogLevelConverter_InvalidValuesReturnFalse(string? value)
        {
            Assert.IsFalse(McpLogLevelConverter.TryConvertFromMcp(value!, out _));
        }

        [DataTestMethod]
        [DataRow(LogLevel.Trace, "debug")]
        [DataRow(LogLevel.Debug, "debug")]
        [DataRow(LogLevel.Information, "info")]
        [DataRow(LogLevel.Warning, "warning")]
        [DataRow(LogLevel.Error, "error")]
        [DataRow(LogLevel.Critical, "critical")]
        [DataRow(LogLevel.None, "debug")]
        [DataRow((LogLevel)99, "info")]
        public void McpLogLevelConverter_AllLogLevelsMapToProtocolValues(LogLevel value, string expected)
        {
            Assert.AreEqual(expected, McpLogLevelConverter.ConvertToMcp(value));
        }

        [TestMethod]
        public void TryValidateEntityRestPath_RejectsOverlongAndColonPaths()
        {
            Assert.IsFalse(RuntimeConfigValidatorUtil.TryValidateEntityRestPath(new string('a', 2049), out string? longError));
            StringAssert.Contains(longError, "maximum allowed length");

            Assert.IsFalse(RuntimeConfigValidatorUtil.TryValidateEntityRestPath("books:archive", out string? colonError));
            StringAssert.Contains(colonError, "reserved character");
        }

        [TestMethod]
        public void DabChangeToken_SignalChangeUpdatesStateAndInvokesCallback()
        {
            DabChangeToken token = new();
            bool callbackInvoked = false;
            using IDisposable registration = token.RegisterChangeCallback(_ => callbackInvoked = true, null);

            Assert.IsTrue(token.ActiveChangeCallbacks);
            Assert.IsFalse(token.HasChanged);
            token.SignalChange();
            Assert.IsTrue(token.HasChanged);
            Assert.IsTrue(callbackInvoked);
        }

        [TestMethod]
        public void MutationResolver_PrimaryConstructorPopulatesProperties()
        {
            MutationResolver resolver = new("id", null!, "database", "container", "fields", "table");

            Assert.AreEqual("id", resolver.Id);
            Assert.IsNull(resolver.OperationType);
            Assert.AreEqual("table", resolver.Table);
        }

        [TestMethod]
        public void MetadataTypeConverter_NonStringInputThrows()
        {
            JsonSerializerOptions options = new();
            options.Converters.Add(new MetadataTypeConverter());

            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<Type>("42", options));
        }
    }
}
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class DatasourceHealthOptionsConverterTests
    {
        /// <summary>
        /// Verifies that invalid data-source response thresholds identify the property being parsed.
        /// </summary>
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        public void Deserialize_InvalidThreshold_IdentifiesThresholdProperty(int thresholdMs)
        {
            string json = $$"""
                { "threshold-ms": {{thresholdMs}} }
                """;

            JsonException exception = Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<DatasourceHealthCheckConfig>(
                    json,
                    RuntimeConfigLoader.GetSerializationOptions()));

            StringAssert.Contains(exception.Message, "threshold-ms");
            StringAssert.DoesNotMatch(exception.Message, new("ttl-seconds"));
        }
    }
}

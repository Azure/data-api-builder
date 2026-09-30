// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <c>BoolJsonConverter</c> which accepts boolean literals as well as
    /// string and numeric representations ("true"/"false"/"1"/"0").
    /// </summary>
    [TestClass]
    public class BoolJsonConverterTests
    {
        private static JsonSerializerOptions GetOptions()
        {
            return RuntimeConfigLoader.GetSerializationOptions();
        }

        [DataTestMethod]
        [DataRow("true", true, DisplayName = "Boolean literal true")]
        [DataRow("false", false, DisplayName = "Boolean literal false")]
        [DataRow("\"true\"", true, DisplayName = "String true")]
        [DataRow("\"false\"", false, DisplayName = "String false")]
        [DataRow("\"True\"", true, DisplayName = "String True mixed case")]
        [DataRow("\"1\"", true, DisplayName = "String 1")]
        [DataRow("\"0\"", false, DisplayName = "String 0")]
        [DataRow("1", true, DisplayName = "Number 1")]
        [DataRow("0", false, DisplayName = "Number 0")]
        public void Deserialize_ValidRepresentations_AreParsed(string json, bool expected)
        {
            bool result = JsonSerializer.Deserialize<bool>(json, GetOptions());

            Assert.AreEqual(expected, result);
        }

        [DataTestMethod]
        [DataRow("\"maybe\"", DisplayName = "Invalid string")]
        [DataRow("2", DisplayName = "Invalid number")]
        [DataRow("null", DisplayName = "Null token")]
        public void Deserialize_InvalidValues_ThrowJsonException(string json)
        {
            Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<bool>(json, GetOptions()));
        }

        [DataTestMethod]
        [DataRow(true, "true")]
        [DataRow(false, "false")]
        public void Serialize_WritesBooleanLiteral(bool value, string expected)
        {
            string json = JsonSerializer.Serialize(value, GetOptions());

            Assert.AreEqual(expected, json);
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <c>CompressionOptionsConverterFactory</c> covering JSON
    /// read/write of HTTP response compression options.
    /// </summary>
    [TestClass]
    public class CompressionOptionsConverterTests
    {
        private static JsonSerializerOptions GetOptions()
        {
            return RuntimeConfigLoader.GetSerializationOptions();
        }

        [DataTestMethod]
        [DataRow("optimal", CompressionLevel.Optimal)]
        [DataRow("fastest", CompressionLevel.Fastest)]
        [DataRow("none", CompressionLevel.None)]
        [DataRow("OPTIMAL", CompressionLevel.Optimal)]
        public void Deserialize_ValidLevel_IsReadCorrectly(string levelStr, CompressionLevel expected)
        {
            string json = $@"{{ ""level"": ""{levelStr}"" }}";

            CompressionOptions options = JsonSerializer.Deserialize<CompressionOptions>(json, GetOptions());

            Assert.IsNotNull(options);
            Assert.AreEqual(expected, options.Level);
            Assert.IsTrue(options.UserProvidedLevel);
        }

        [TestMethod]
        public void Deserialize_NoLevel_UsesDefaultAndNotUserProvided()
        {
            CompressionOptions options = JsonSerializer.Deserialize<CompressionOptions>("{}", GetOptions());

            Assert.IsNotNull(options);
            Assert.AreEqual(CompressionOptions.DEFAULT_LEVEL, options.Level);
            Assert.IsFalse(options.UserProvidedLevel);
        }

        [TestMethod]
        public void Deserialize_UnknownProperty_IsSkipped()
        {
            string json = @"{ ""unknown"": { ""nested"": true }, ""level"": ""fastest"" }";

            CompressionOptions options = JsonSerializer.Deserialize<CompressionOptions>(json, GetOptions());

            Assert.IsNotNull(options);
            Assert.AreEqual(CompressionLevel.Fastest, options.Level);
        }

        [TestMethod]
        public void Deserialize_InvalidLevel_ThrowsJsonException()
        {
            string json = @"{ ""level"": ""super-fast"" }";

            Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<CompressionOptions>(json, GetOptions()));
        }

        [TestMethod]
        public void Deserialize_NullToken_ReturnsNull()
        {
            CompressionOptions options = JsonSerializer.Deserialize<CompressionOptions>("null", GetOptions());

            Assert.IsNull(options);
        }

        [TestMethod]
        public void Deserialize_NonObjectToken_ThrowsJsonException()
        {
            Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<CompressionOptions>("\"not-an-object\"", GetOptions()));
        }

        [TestMethod]
        public void Serialize_UserProvidedLevel_WritesLowercaseLevel()
        {
            CompressionOptions options = new(CompressionLevel.Fastest);

            string json = JsonSerializer.Serialize(options, GetOptions());
            JObject jObject = JObject.Parse(json);

            Assert.IsTrue(jObject.ContainsKey("level"));
            Assert.AreEqual("fastest", jObject["level"].Value<string>());
        }

        [TestMethod]
        public void Serialize_NotUserProvided_WritesEmptyObject()
        {
            CompressionOptions options = new();

            string json = JsonSerializer.Serialize(options, GetOptions());
            JObject jObject = JObject.Parse(json);

            Assert.AreEqual(0, jObject.Count);
        }

        [TestMethod]
        public void RoundTrip_PreservesLevel()
        {
            CompressionOptions original = new(CompressionLevel.None);

            string json = JsonSerializer.Serialize(original, GetOptions());
            CompressionOptions result = JsonSerializer.Deserialize<CompressionOptions>(json, GetOptions());

            Assert.AreEqual(original.Level, result.Level);
            Assert.IsTrue(result.UserProvidedLevel);
        }
    }
}

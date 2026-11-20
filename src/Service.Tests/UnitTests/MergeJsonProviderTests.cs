// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="MergeJsonProvider"/> which merges two JSON strings.
    /// Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class MergeJsonProviderTests
    {
        [TestMethod]
        public void Merge_OverridesScalarProperty()
        {
            string result = MergeJsonProvider.Merge(@"{ ""a"": 1, ""b"": 2 }", @"{ ""b"": 99 }");
            JObject merged = JObject.Parse(result);

            Assert.AreEqual(1, merged["a"].Value<int>());
            Assert.AreEqual(99, merged["b"].Value<int>());
        }

        [TestMethod]
        public void Merge_RecursivelyMergesNestedObjects()
        {
            string original = @"{ ""outer"": { ""x"": 1, ""y"": 2 } }";
            string overriding = @"{ ""outer"": { ""y"": 20, ""z"": 30 } }";

            JObject merged = JObject.Parse(MergeJsonProvider.Merge(original, overriding));

            Assert.AreEqual(1, merged["outer"]["x"].Value<int>());
            Assert.AreEqual(20, merged["outer"]["y"].Value<int>());
            Assert.AreEqual(30, merged["outer"]["z"].Value<int>());
        }

        [TestMethod]
        public void Merge_ReplacesArraysRatherThanMerging()
        {
            string original = @"{ ""items"": [1, 2, 3] }";
            string overriding = @"{ ""items"": [9] }";

            JObject merged = JObject.Parse(MergeJsonProvider.Merge(original, overriding));

            JArray items = (JArray)merged["items"];
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(9, items[0].Value<int>());
        }

        [TestMethod]
        public void Merge_AddsPropertiesUniqueToSecondDocument()
        {
            JObject merged = JObject.Parse(MergeJsonProvider.Merge(@"{ ""a"": 1 }", @"{ ""b"": 2 }"));

            Assert.AreEqual(1, merged["a"].Value<int>());
            Assert.AreEqual(2, merged["b"].Value<int>());
        }

        [TestMethod]
        public void Merge_NullInOverride_KeepsOriginalValue()
        {
            JObject merged = JObject.Parse(MergeJsonProvider.Merge(@"{ ""a"": 1 }", @"{ ""a"": null }"));

            Assert.AreEqual(1, merged["a"].Value<int>());
        }

        [TestMethod]
        public void Merge_TypeMismatch_OverridesWithSecondValue()
        {
            // Original 'a' is an object, override is a string -> override wins.
            JObject merged = JObject.Parse(MergeJsonProvider.Merge(@"{ ""a"": { ""x"": 1 } }", @"{ ""a"": ""str"" }"));

            Assert.AreEqual("str", merged["a"].Value<string>());
        }

        [TestMethod]
        public void Merge_RootTypeMismatch_ReturnsOriginalUnchanged()
        {
            string original = @"{ ""a"": 1 }";

            string result = MergeJsonProvider.Merge(original, "[1, 2]");

            Assert.AreEqual(original, result);
        }

        [TestMethod]
        public void Merge_RootArrays_OverridesWithSecondArray()
        {
            JArray merged = JArray.Parse(MergeJsonProvider.Merge("[1, 2, 3]", "[7, 8]"));

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(7, merged[0].Value<int>());
        }

        [TestMethod]
        public void Merge_NonContainerOriginal_ThrowsInvalidOperationException()
        {
            Assert.ThrowsException<InvalidOperationException>(
                () => MergeJsonProvider.Merge("42", "43"));
        }
    }
}

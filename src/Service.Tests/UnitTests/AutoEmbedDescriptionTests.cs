// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Behavior tests for <see cref="AutoEmbedDescription.Append"/> — the centralized
    /// helper that surfaces the auto-embed indicator in REST OpenAPI, GraphQL schema,
    /// and MCP tool descriptions. Pins the contract so every callsite (currently 4
    /// in production code) renders consistently.
    /// </summary>
    [TestClass]
    public class AutoEmbedDescriptionTests
    {
        /// <summary>
        /// Exhaustive matrix over (baseDescription × isAutoEmbed).
        /// - When isAutoEmbed=true: suffix is appended (treating null base as empty string).
        /// - When isAutoEmbed=false: base is returned verbatim, preserving null vs empty
        ///   so OpenAPI schema callers can distinguish "no description" from "empty".
        /// </summary>
        [DataTestMethod]
        [DataRow(null, true, AutoEmbedDescription.Suffix,
            DisplayName = "null base + autoEmbed=true → suffix only (null treated as empty)")]
        [DataRow("foo", true, "foo" + AutoEmbedDescription.Suffix,
            DisplayName = "non-null base + autoEmbed=true → base + suffix")]
        [DataRow("", true, AutoEmbedDescription.Suffix,
            DisplayName = "empty base + autoEmbed=true → suffix only")]
        [DataRow("foo", false, "foo",
            DisplayName = "non-null base + autoEmbed=false → unchanged")]
        [DataRow(null, false, null,
            DisplayName = "null base + autoEmbed=false → null preserved")]
        [DataRow("", false, "",
            DisplayName = "empty base + autoEmbed=false → empty preserved")]
        public void Append_BehavesPerSpec(string? baseDescription, bool isAutoEmbed, string? expected)
        {
            string? actual = AutoEmbedDescription.Append(baseDescription, isAutoEmbed);
            Assert.AreEqual(expected, actual);
        }
    }
}

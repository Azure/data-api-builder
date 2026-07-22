// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Specialized;
using System.Text.Json;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the pure pagination helpers on <see cref="SqlPaginationUtil"/>.
    /// </summary>
    [TestClass]
    public class SqlPaginationUtilUnitTests
    {
        [TestMethod]
        public void Base64Encode_Decode_Roundtrip()
        {
            const string original = "Hello, World! [{\"id\":1}]";
            string encoded = SqlPaginationUtil.Base64Encode(original);
            Assert.AreEqual(original, SqlPaginationUtil.Base64Decode(encoded));
        }

        [DataTestMethod]
        [DataRow("abc", "YWJj")]
        [DataRow("", "")]
        public void Base64Encode_KnownValues(string plain, string expected)
        {
            Assert.AreEqual(expected, SqlPaginationUtil.Base64Encode(plain));
        }

        [DataTestMethod]
        // first supplied: hasNext when record count exceeds first.
        [DataRow("[1,2,3]", 2, 100u, 100u, true, DisplayName = "first=2, 3 records -> hasNext")]
        [DataRow("[1,2,3]", 3, 100u, 100u, false, DisplayName = "first=3, 3 records -> no next")]
        // first == -1 means client requested max page size.
        [DataRow("[1,2,3]", -1, 100u, 5u, false, DisplayName = "first=-1 uses maxPageSize 5, 3 records -> no next")]
        [DataRow("[1,2,3,4,5,6]", -1, 100u, 5u, true, DisplayName = "first=-1 uses maxPageSize 5, 6 records -> hasNext")]
        public void HasNext_WithFirst(string json, int first, uint defaultPageSize, uint maxPageSize, bool expected)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.AreEqual(expected, SqlPaginationUtil.HasNext(doc.RootElement, first, defaultPageSize, maxPageSize));
        }

        [DataTestMethod]
        // first null falls back to default page size.
        [DataRow("[1,2,3]", 2u, true, DisplayName = "default=2, 3 records -> hasNext")]
        [DataRow("[1,2,3]", 5u, false, DisplayName = "default=5, 3 records -> no next")]
        public void HasNext_WithoutFirst_UsesDefaultPageSize(string json, uint defaultPageSize, bool expected)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.AreEqual(expected, SqlPaginationUtil.HasNext(doc.RootElement, first: null, defaultPageSize, maxPageSize: 100u));
        }

        [TestMethod]
        public void BuildQueryStringWithAfterToken_NullParams_AppendsAfter()
        {
            string result = SqlPaginationUtil.BuildQueryStringWithAfterToken(null, "tok123");
            Assert.AreEqual("?$after=tok123", result);
        }

        [TestMethod]
        public void BuildQueryStringWithAfterToken_EmptyPayload_NoAfterAppended()
        {
            string result = SqlPaginationUtil.BuildQueryStringWithAfterToken(null, string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void BuildQueryStringWithAfterToken_ReplacesExistingAfter()
        {
            NameValueCollection parameters = new() { { "$after", "oldToken" } };
            string result = SqlPaginationUtil.BuildQueryStringWithAfterToken(parameters, "newToken");

            StringAssert.Contains(result, "$after=newToken");
            Assert.IsFalse(result.Contains("oldToken"), "Existing $after token should be removed.");
        }

        [TestMethod]
        public void BuildQueryStringWithAfterToken_PreservesOtherParams()
        {
            NameValueCollection parameters = new() { { "$first", "5" } };
            string result = SqlPaginationUtil.BuildQueryStringWithAfterToken(parameters, "tok");

            StringAssert.StartsWith(result, "?");
            StringAssert.Contains(result, "$after=tok");
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void FormatQueryString_NullOrEmpty_ReturnsEmpty(bool useNull)
        {
            NameValueCollection? parameters = useNull ? null : new NameValueCollection();
            Assert.AreEqual(string.Empty, SqlPaginationUtil.FormatQueryString(parameters));
        }

        [TestMethod]
        public void FormatQueryString_WithParameter_ReturnsPrefixedQueryString()
        {
            NameValueCollection parameters = new() { { "$filter", "id eq 1" } };
            string result = SqlPaginationUtil.FormatQueryString(parameters);

            StringAssert.StartsWith(result, "?");
            StringAssert.Contains(result, "filter");
        }

        [TestMethod]
        public void GetConsolidatedNextLinkForPagination_Absolute_ReturnsFullUri()
        {
            JsonElement result = SqlPaginationUtil.GetConsolidatedNextLinkForPagination(
                "http://localhost/api/Book", "?$after=tok", isNextLinkRelative: false);

            JsonElement.ArrayEnumerator enumerator = result.EnumerateArray();
            enumerator.MoveNext();
            string nextLink = enumerator.Current.GetProperty("nextLink").GetString()!;

            StringAssert.Contains(nextLink, "http://localhost/api/Book");
            StringAssert.Contains(nextLink, "$after=tok");
        }

        [TestMethod]
        public void GetConsolidatedNextLinkForPagination_Relative_ReturnsPathAndQuery()
        {
            JsonElement result = SqlPaginationUtil.GetConsolidatedNextLinkForPagination(
                "http://localhost/api/Book", "?$after=tok", isNextLinkRelative: true);

            JsonElement.ArrayEnumerator enumerator = result.EnumerateArray();
            enumerator.MoveNext();
            string nextLink = enumerator.Current.GetProperty("nextLink").GetString()!;

            StringAssert.StartsWith(nextLink, "/api/Book");
            Assert.IsFalse(nextLink.Contains("localhost"), "Relative link should not contain the host.");
        }
    }
}

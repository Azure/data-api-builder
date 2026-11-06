// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using System.Text.Json;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Microsoft.AspNetCore.Http;
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

        /// <summary>
        /// Verifies string, number, null, and Boolean cursor values resolve while object and array values are rejected.
        /// </summary>
        [TestMethod]
        public void TryResolveJsonElementToScalarVariable_HandlesEveryJsonKind()
        {
            using JsonDocument document = JsonDocument.Parse("[\"text\",12.5,null,true,false,{},[]]");
            JsonElement.ArrayEnumerator elements = document.RootElement.EnumerateArray();

            elements.MoveNext();
            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(elements.Current, out object? text));
            Assert.AreEqual("text", text);
            elements.MoveNext();
            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(elements.Current, out object? number));
            Assert.AreEqual(12.5, number);
            elements.MoveNext();
            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(elements.Current, out object? nullValue));
            Assert.IsNull(nullValue);
            elements.MoveNext();
            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(elements.Current, out object? trueValue));
            Assert.AreEqual(true, trueValue);
            elements.MoveNext();
            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(elements.Current, out object? falseValue));
            Assert.AreEqual(false, falseValue);
            elements.MoveNext();
            Assert.IsFalse(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(elements.Current, out _));
            elements.MoveNext();
            Assert.IsFalse(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(elements.Current, out _));
        }

        [TestMethod]
        public void MakeCursorFromJsonElement_IncludesOrderByAndRemainingPrimaryKey()
        {
            using JsonDocument document = JsonDocument.Parse("{\"name\":\"Ada\",\"id\":7}");

            string cursor = SqlPaginationUtil.MakeCursorFromJsonElement(
                document.RootElement,
                new List<string> { "id" },
                new List<OrderByColumn> { new("dbo", "authors", "name", "a", OrderBy.DESC) },
                entityName: "Author");
            string decoded = SqlPaginationUtil.Base64Decode(cursor);

            StringAssert.Contains(decoded, "name");
            StringAssert.Contains(decoded, "Ada");
            StringAssert.Contains(decoded, "id");
            StringAssert.Contains(decoded, "\"Direction\":1");
        }

        /// <summary>
        /// Integers larger than 2^53 must survive cursor encode/decode as the exact BIGINT.
        /// <see cref="SqlPaginationUtil.TryResolveJsonElementToScalarVariable"/> used to store every
        /// JSON number as a double, so the next-page $after token no longer parsed as Int64.
        /// </summary>
        [DataTestMethod]
        [DataRow(5123692796229827916L, DisplayName = "19-digit BIGINT beyond IEEE-754 integer precision")]
        [DataRow(9007199254740993L, DisplayName = "2^53 + 1")]
        public void MakeCursorFromJsonElement_PreservesExactIntegerPrimaryKey(long expected)
        {
            using JsonDocument document = JsonDocument.Parse($"{{\"id\":{expected}}}");

            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(
                document.RootElement.GetProperty("id"),
                out object? scalar));
            Assert.AreEqual(expected, scalar);

            string cursor = SqlPaginationUtil.MakeCursorFromJsonElement(
                document.RootElement,
                new List<string> { "id" },
                orderByColumns: null,
                entityName: "Entity");
            string decoded = SqlPaginationUtil.Base64Decode(cursor);

            using JsonDocument cursorDocument = JsonDocument.Parse(decoded);
            string fieldValueText = cursorDocument.RootElement[0].GetProperty("FieldValue").GetRawText();

            object parsed = InvokeParseParamAsSystemType(fieldValueText, typeof(long));
            Assert.AreEqual(expected, parsed);
        }

        /// <summary>
        /// Ordinary ints and floats still resolve to long/double so existing cursor and filter
        /// parsing through <see cref="BaseSqlQueryStructure"/> is unchanged.
        /// </summary>
        [TestMethod]
        public void TryResolveJsonElementToScalarVariable_PreservesNormalIntAndFloat()
        {
            using JsonDocument document = JsonDocument.Parse("{\"id\":7,\"score\":12.5}");

            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(
                document.RootElement.GetProperty("id"),
                out object? id));
            Assert.AreEqual(7L, id);
            Assert.AreEqual(7, InvokeParseParamAsSystemType(id!.ToString()!, typeof(int)));

            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(
                document.RootElement.GetProperty("score"),
                out object? score));
            Assert.AreEqual(12.5d, score);
            Assert.AreEqual(12.5d, InvokeParseParamAsSystemType(score!.ToString()!, typeof(double)));
        }

        /// <summary>
        /// Int64 boundaries resolve to exact longs.
        /// </summary>
        [DataTestMethod]
        [DataRow(long.MinValue, DisplayName = "long.MinValue")]
        [DataRow(long.MaxValue, DisplayName = "long.MaxValue")]
        public void TryResolveJsonElementToScalarVariable_PreservesInt64Bounds(long expected)
        {
            using JsonDocument document = JsonDocument.Parse($"{{\"id\":{expected}}}");

            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(
                document.RootElement.GetProperty("id"),
                out object? scalar));
            Assert.AreEqual(expected, scalar);
        }

        /// <summary>
        /// Tiny nonzero fractions must stay doubles and must not round to zero,
        /// otherwise ORDER BY pagination boundaries shift.
        /// </summary>
        [DataTestMethod]
        [DataRow("1e-30", 1e-30, DisplayName = "positive tiny fraction")]
        [DataRow("-1e-30", -1e-30, DisplayName = "negative tiny fraction")]
        public void TryResolveJsonElementToScalarVariable_KeepsTinyFractionsAsDouble(string json, double expected)
        {
            using JsonDocument document = JsonDocument.Parse($"{{\"score\":{json}}}");

            Assert.IsTrue(SqlPaginationUtil.TryResolveJsonElementToScalarVariable(
                document.RootElement.GetProperty("score"),
                out object? scalar));
            Assert.IsInstanceOfType(scalar, typeof(double));
            Assert.AreEqual(expected, (double)scalar!);
            Assert.AreNotEqual(0d, (double)scalar!);
        }

        [TestMethod]
        public void MakeCursorFromJsonElement_GroupByOmitsPrimaryKeys()
        {
            using JsonDocument document = JsonDocument.Parse("{\"total\":12}");

            string decoded = SqlPaginationUtil.Base64Decode(SqlPaginationUtil.MakeCursorFromJsonElement(
                document.RootElement,
                new List<string> { "missing" },
                new List<OrderByColumn> { new("", "", "total", "", OrderBy.ASC) },
                isGroupByQuery: true));

            StringAssert.Contains(decoded, "total");
            Assert.IsFalse(decoded.Contains("missing"));
        }

        [TestMethod]
        public void MakeCursorFromJsonElement_RejectsNonScalarCursorValue()
        {
            using JsonDocument document = JsonDocument.Parse("{\"id\":{}}");

            Assert.ThrowsException<DataApiBuilderException>(() => SqlPaginationUtil.MakeCursorFromJsonElement(
                document.RootElement,
                new List<string> { "id" },
                orderByColumns: null));
        }

        [TestMethod]
        public void ConstructBaseUriForPagination_UsesValidForwardedHeadersAndBaseRoute()
        {
            DefaultHttpContext context = new();
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("internal:5000");
            context.Request.Path = "/api/books";
            context.Request.Headers["X-Forwarded-Proto"] = " HTTPS ";
            context.Request.Headers["X-Forwarded-Host"] = " example.com:8443 ";

            string result = SqlPaginationUtil.ConstructBaseUriForPagination(context, "/gateway");

            Assert.AreEqual("https://example.com:8443/gateway/api/books", result);
        }

        [TestMethod]
        public void ConstructBaseUriForPagination_InvalidForwardedHeadersFallBackToRequest()
        {
            DefaultHttpContext context = new();
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost:5000");
            context.Request.Path = "/api/books";
            context.Request.Headers["X-Forwarded-Proto"] = "ftp";
            context.Request.Headers["X-Forwarded-Host"] = "bad host@value";

            Assert.AreEqual("http://localhost:5000/api/books", SqlPaginationUtil.ConstructBaseUriForPagination(context));
        }

        [TestMethod]
        public void FormatQueryString_IgnoresBlankKeysAndValues()
        {
            NameValueCollection parameters = new()
            {
                { " ", "ignored" },
                { "$filter", " " },
                { "$first", "10" }
            };

            string result = SqlPaginationUtil.FormatQueryString(parameters);

            StringAssert.Contains(result, "$first=10");
            Assert.IsFalse(result.Contains("ignored"));
            Assert.IsFalse(result.Contains("filter"));
        }

        private static object InvokeParseParamAsSystemType(string value, Type targetType)
        {
            MethodInfo method = typeof(BaseSqlQueryStructure).GetMethod(
                "ParseParamAsSystemType",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            return method.Invoke(null, new object[] { value, targetType })!;
        }
    }
}

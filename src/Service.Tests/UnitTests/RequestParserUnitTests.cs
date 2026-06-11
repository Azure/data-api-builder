// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Reflection;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Test class for RequestParser utility methods.
    /// Specifically tests the ExtractRawQueryParameter method which preserves
    /// URL encoding for special characters in query parameters.
    /// </summary>
    [TestClass]
    public class RequestParserUnitTests
    {
        private const string DEFAULT_ENTITY = "Book";
        private const string DEFAULT_SCHEMA = "dbo";

        /// <summary>
        /// Tests that ExtractRawQueryParameter correctly extracts URL-encoded
        /// parameter values, preserving special characters like ampersand (&).
        /// </summary>
        [DataTestMethod]
        [DataRow("?$filter=region%20eq%20%27filter%20%26%20test%27", "$filter", "region%20eq%20%27filter%20%26%20test%27", DisplayName = "Extract filter with encoded ampersand (&)")]
        [DataRow("?$filter=title%20eq%20%27A%20%26%20B%27&$select=id", "$filter", "title%20eq%20%27A%20%26%20B%27", DisplayName = "Extract filter with ampersand and other params")]
        [DataRow("?$select=id&$filter=name%20eq%20%27test%27", "$filter", "name%20eq%20%27test%27", DisplayName = "Extract filter when not first parameter")]
        [DataRow("?$orderby=name%20asc", "$orderby", "name%20asc", DisplayName = "Extract orderby parameter")]
        [DataRow("?param1=value1&param2=value%26with%26ampersands", "param2", "value%26with%26ampersands", DisplayName = "Extract parameter with multiple ampersands")]
        [DataRow("$filter=title%20eq%20%27test%27", "$filter", "title%20eq%20%27test%27", DisplayName = "Extract without leading question mark")]
        [DataRow("?$filter=", "$filter", "", DisplayName = "Extract empty filter value")]
        [DataRow("?$filter=name%20eq%20%27test%3D123%27", "$filter", "name%20eq%20%27test%3D123%27", DisplayName = "Extract filter with encoded equals sign (=)")]
        [DataRow("?$filter=url%20eq%20%27http%3A%2F%2Fexample.com%3Fkey%3Dvalue%27", "$filter", "url%20eq%20%27http%3A%2F%2Fexample.com%3Fkey%3Dvalue%27", DisplayName = "Extract filter with encoded URL (: / ?)")]
        [DataRow("?$filter=text%20eq%20%27A%2BB%27", "$filter", "text%20eq%20%27A%2BB%27", DisplayName = "Extract filter with encoded plus sign (+)")]
        [DataRow("?$filter=value%20eq%20%2750%25%27", "$filter", "value%20eq%20%2750%25%27", DisplayName = "Extract filter with encoded percent sign (%)")]
        [DataRow("?$filter=tag%20eq%20%27%23hashtag%27", "$filter", "tag%20eq%20%27%23hashtag%27", DisplayName = "Extract filter with encoded hash (#)")]
        [DataRow("?$filter=expr%20eq%20%27a%3Cb%3Ed%27", "$filter", "expr%20eq%20%27a%3Cb%3Ed%27", DisplayName = "Extract filter with encoded less-than and greater-than (< >)")]
        public void ExtractRawQueryParameter_PreservesEncoding(string queryString, string parameterName, string expectedValue)
        {
            // Call the internal method directly (no reflection needed)
            string result = RequestParser.ExtractRawQueryParameter(queryString, parameterName);

            Assert.AreEqual(expectedValue, result,
                $"Expected '{expectedValue}' but got '{result}' for parameter '{parameterName}' in query '{queryString}'");
        }

        /// <summary>
        /// Tests that ExtractRawQueryParameter returns null when parameter is not found.
        /// </summary>
        [DataTestMethod]
        [DataRow("?$filter=test", "$orderby", DisplayName = "Parameter not in query string")]
        [DataRow("", "$filter", DisplayName = "Empty query string")]
        [DataRow(null, "$filter", DisplayName = "Null query string")]
        [DataRow("?otherParam=value", "$filter", DisplayName = "Different parameter")]
        public void ExtractRawQueryParameter_ReturnsNull_WhenParameterNotFound(string queryString, string parameterName)
        {
            // Call the internal method directly (no reflection needed)
            string result = RequestParser.ExtractRawQueryParameter(queryString, parameterName);

            Assert.IsNull(result,
                $"Expected null but got '{result}' for parameter '{parameterName}' in query '{queryString}'");
        }

        /// <summary>
        /// Tests that ExtractRawQueryParameter handles edge cases correctly:
        /// - Duplicate parameters (returns first occurrence)
        /// - Case-insensitive parameter name matching
        /// - Malformed query strings with unencoded ampersands
        /// </summary>
        [DataTestMethod]
        [DataRow("?$filter=value&$filter=anothervalue", "$filter", "value", DisplayName = "Multiple same parameters - returns first")]
        [DataRow("?$FILTER=value", "$filter", "value", DisplayName = "Case insensitive parameter matching")]
        [DataRow("?param=value1&value2", "param", "value1", DisplayName = "Value with unencoded ampersand after parameter")]
        public void ExtractRawQueryParameter_HandlesEdgeCases(string queryString, string parameterName, string expectedValue)
        {
            // Call the internal method directly (no reflection needed)
            string result = RequestParser.ExtractRawQueryParameter(queryString, parameterName);

            Assert.AreEqual(expectedValue, result,
                $"Expected '{expectedValue}' but got '{result}' for parameter '{parameterName}' in query '{queryString}'");
        }

        [TestMethod]
        public void ParseQueryString_SetsSemanticInputs_ForUserRequest()
        {
            FindRequestContext context = new(
                entityName: DEFAULT_ENTITY,
                dbo: new DatabaseTable(DEFAULT_SCHEMA, DEFAULT_ENTITY),
                isList: true)
            {
                RawQueryString = "?$semantic_search=wireless%20headphones&$semantic_threshold=0.83"
            };

            context.ParsedQueryString.Add(RequestParser.SEMANTIC_SEARCH_URL, "wireless headphones");
            context.ParsedQueryString.Add(RequestParser.SEMANTIC_THRESHOLD_URL, "0.83");

            RequestParser.ParseQueryString(context, new Mock<ISqlMetadataProvider>().Object);

            Assert.AreEqual("wireless headphones", context.SemanticSearch);
            Assert.AreEqual(0.83, context.SemanticThreshold);
        }

        [DataTestMethod]
        [DataRow("-0.01")]
        [DataRow("1.01")]
        [DataRow("not-a-number")]
        public void ParseQueryString_RejectsInvalidSemanticThreshold_ForUserRequest(string threshold)
        {
            FindRequestContext context = new(
                entityName: DEFAULT_ENTITY,
                dbo: new DatabaseTable(DEFAULT_SCHEMA, DEFAULT_ENTITY),
                isList: true)
            {
                RawQueryString = $"?$semantic_threshold={threshold}"
            };

            context.ParsedQueryString.Add(RequestParser.SEMANTIC_THRESHOLD_URL, threshold);

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestParser.ParseQueryString(context, new Mock<ISqlMetadataProvider>().Object));

            StringAssert.Contains(ex.Message, "semantic_threshold must be a decimal value between 0.0 and 1.0.");
        }

        [DataTestMethod]
        [DataRow("?$orderby=semantic_distance%20asc")]
        [DataRow("?$orderby=Semantic_Distance%20desc")]
        public void ParseQueryString_RejectsOrderBySemanticDistance_ForUserRequest(string rawQuery)
        {
            FindRequestContext context = new(
                entityName: DEFAULT_ENTITY,
                dbo: new DatabaseTable(DEFAULT_SCHEMA, DEFAULT_ENTITY),
                isList: true)
            {
                RawQueryString = rawQuery
            };

            context.ParsedQueryString.Add(RequestParser.SORT_URL, rawQuery.Contains("desc", StringComparison.OrdinalIgnoreCase) ? "Semantic_Distance desc" : "semantic_distance asc");

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestParser.ParseQueryString(context, new Mock<ISqlMetadataProvider>().Object));

            StringAssert.Contains(ex.Message, "semantic_distance cannot be used in orderBy.");
        }

        [DataTestMethod]
        [DataRow("semantic_distance asc", true)]
        [DataRow("Semantic_Distance desc", true)]
        [DataRow("semantic_distance_score asc", false)]
        [DataRow("title asc,semantic_distance_score desc", false)]
        public void ContainsSemanticDistanceOrderByToken_UsesExactColumnMatch(string rawSortValue, bool expected)
        {
            MethodInfo method = typeof(RequestParser).GetMethod(
                "ContainsSemanticDistanceOrderByToken",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "Expected private helper method to exist.");
            bool actual = (bool)method!.Invoke(null, new object[] { rawSortValue })!;
            Assert.AreEqual(expected, actual);
        }
    }
}

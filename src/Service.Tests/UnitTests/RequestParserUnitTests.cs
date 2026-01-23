// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Core.Parsers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        /// <summary>
        /// Tests that ExtractRawQueryParameter correctly extracts URL-encoded
        /// parameter values, preserving special characters like ampersand (&).
        /// </summary>
        [DataTestMethod]
        [DataRow("?$filter=region%20eq%20%27filter%20%26%20test%27", "$filter", "region%20eq%20%27filter%20%26%20test%27", DisplayName = "Extract filter with encoded ampersand")]
        [DataRow("?$filter=title%20eq%20%27A%20%26%20B%27&$select=id", "$filter", "title%20eq%20%27A%20%26%20B%27", DisplayName = "Extract filter with ampersand and other params")]
        [DataRow("?$select=id&$filter=name%20eq%20%27test%27", "$filter", "name%20eq%20%27test%27", DisplayName = "Extract filter when not first parameter")]
        [DataRow("?$orderby=name%20asc", "$orderby", "name%20asc", DisplayName = "Extract orderby parameter")]
        [DataRow("?param1=value1&param2=value%26with%26ampersands", "param2", "value%26with%26ampersands", DisplayName = "Extract parameter with multiple ampersands")]
        [DataRow("$filter=title%20eq%20%27test%27", "$filter", "title%20eq%20%27test%27", DisplayName = "Extract without leading question mark")]
        [DataRow("?$filter=", "$filter", "", DisplayName = "Extract empty filter value")]
        public void ExtractRawQueryParameter_PreservesEncoding(string queryString, string parameterName, string expectedValue)
        {
            // Use reflection to call the private static method
            MethodInfo? method = typeof(RequestParser).GetMethod(
                "ExtractRawQueryParameter",
                BindingFlags.NonPublic | BindingFlags.Static);
            
            Assert.IsNotNull(method, "ExtractRawQueryParameter method should exist");

            string? result = (string?)method.Invoke(null, new object[] { queryString, parameterName });
            
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
        public void ExtractRawQueryParameter_ReturnsNull_WhenParameterNotFound(string? queryString, string parameterName)
        {
            // Use reflection to call the private static method
            MethodInfo? method = typeof(RequestParser).GetMethod(
                "ExtractRawQueryParameter",
                BindingFlags.NonPublic | BindingFlags.Static);
            
            Assert.IsNotNull(method, "ExtractRawQueryParameter method should exist");

            string? result = (string?)method.Invoke(null, new object?[] { queryString, parameterName });
            
            Assert.IsNull(result, 
                $"Expected null but got '{result}' for parameter '{parameterName}' in query '{queryString}'");
        }

        /// <summary>
        /// Tests that ExtractRawQueryParameter handles edge cases correctly.
        /// </summary>
        [DataTestMethod]
        [DataRow("?$filter=value&$filter=anothervalue", "$filter", "value", DisplayName = "Multiple same parameters - returns first")]
        [DataRow("?$FILTER=value", "$filter", "value", DisplayName = "Case insensitive parameter matching")]
        [DataRow("?param=value1&value2", "param", "value1", DisplayName = "Value with unencoded ampersand after parameter")]
        public void ExtractRawQueryParameter_HandlesEdgeCases(string queryString, string parameterName, string expectedValue)
        {
            // Use reflection to call the private static method
            MethodInfo? method = typeof(RequestParser).GetMethod(
                "ExtractRawQueryParameter",
                BindingFlags.NonPublic | BindingFlags.Static);
            
            Assert.IsNotNull(method, "ExtractRawQueryParameter method should exist");

            string? result = (string?)method.Invoke(null, new object[] { queryString, parameterName });
            
            Assert.AreEqual(expectedValue, result, 
                $"Expected '{expectedValue}' but got '{result}' for parameter '{parameterName}' in query '{queryString}'");
        }
    }
}

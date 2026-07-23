// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="HttpStatusCodeExtensions"/>. Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class HttpStatusCodeExtensionsTests
    {
        [DataTestMethod]
        [DataRow(HttpStatusCode.BadRequest, true, DisplayName = "400 is a client error")]
        [DataRow(HttpStatusCode.NotFound, true, DisplayName = "404 is a client error")]
        [DataRow(HttpStatusCode.Forbidden, true, DisplayName = "403 is a client error")]
        [DataRow(HttpStatusCode.OK, false, DisplayName = "200 is not a client error")]
        [DataRow(HttpStatusCode.InternalServerError, false, DisplayName = "500 is not a client error")]
        [DataRow(HttpStatusCode.MovedPermanently, false, DisplayName = "301 is not a client error")]
        public void IsClientError_ReturnsExpected(HttpStatusCode statusCode, bool expected)
        {
            Assert.AreEqual(expected, statusCode.IsClientError());
        }
    }
}

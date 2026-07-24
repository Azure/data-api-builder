// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Authentication
{
    [TestClass]
    public class CustomJwtRoleClaimExtractorUnitTests
    {
        [DataTestMethod]
        [DataRow(@"{ ""roles"": [""Admins"", ""Users""] }", null, null, "Admins,Users", DisplayName = "Default roles array")]
        [DataRow(@"{ ""roles"": [""Admins""] }", null, "array", "Admins", DisplayName = "Omitted rolesPath")]
        [DataRow(@"{ ""groups"": [""Admins""] }", "groups", null, "Admins", DisplayName = "Omitted rolesFormat")]
        [DataRow(@"{ ""realm_access"": { ""roles"": [""editor"", ""contributor""] } }", "realm_access.roles", "array", "editor,contributor", DisplayName = "Nested dot path")]
        [DataRow(@"{ ""https://example.com/roles"": [""admin"", ""writer""] }", "https://example.com/roles", "array", "admin,writer", DisplayName = "URL claim key")]
        [DataRow(@"{ ""cognito:groups"": [""Staff"", ""Managers""] }", "cognito:groups", "array", "Staff,Managers", DisplayName = "Colon claim key")]
        [DataRow(@"{ ""app.roles"": [""admin"", ""writer""] }", "['app.roles']", "array", "admin,writer", DisplayName = "Bracket literal claim key with dots")]
        [DataRow(@"{ ""https://example.com/roles"": [""admin""] }", "['https://example.com/roles']", "array", "admin", DisplayName = "Bracket literal URL claim key")]
        [DataRow(@"{ ""resource_access"": { ""dab-api"": { ""roles"": [""admin""] } } }", "resource_access['dab-api'].roles", "array", "admin", DisplayName = "Mixed dot and bracket path")]
        [DataRow(@"{ ""role"": ""superadmin"" }", "role", "string", "superadmin", DisplayName = "String format")]
        [DataRow(@"{ ""scope"": ""openid read:orders write:orders"" }", "scope", "delimited-string", "openid,read:orders,write:orders", DisplayName = "Delimited string default delimiter")]
        [DataRow(@"{ ""scope"": ""admin"" }", "scope", "delimited-string", "admin", DisplayName = "Delimited string without delimiter")]
        [DataRow(@"{ ""roles"": [] }", "roles", "array", "", DisplayName = "Empty array")]
        [DataRow(@"{ ""role"": """" }", "role", "string", "", DisplayName = "Empty string")]
        [DataRow(@"{ ""roles"": [""admin"", ""admin"", ""Admin""] }", "roles", "array", "admin,Admin", DisplayName = "Duplicate roles are ordinal and case-sensitive")]
        [DataRow(@"{ ""roles"": ["" admin "", ""writer"", "" ""] }", "roles", "array", "admin,writer", DisplayName = "Trimmed roles and empty values removed")]
        [DataRow(@"{ ""scope"": "" admin writer  auditor"" }", "scope", "delimited-string", "admin,writer,auditor", DisplayName = "Delimited roles are normalized")]
        public void TryExtractRoles_Success(string payloadJson, string rolesPath, string rolesFormat, string expectedRolesCsv)
        {
            bool result = CustomJwtRoleClaimExtractor.TryExtractRoles(
                payloadJson,
                rolesPath ?? JwtOptions.DEFAULT_ROLES_PATH,
                rolesFormat ?? JwtOptions.DEFAULT_ROLES_FORMAT,
                NullLogger.Instance,
                out IReadOnlyList<string> roles);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(
                expectedRolesCsv.Length == 0 ? new List<string>() : new List<string>(expectedRolesCsv.Split(',')),
                new List<string>(roles));
        }

        [TestMethod]
        public void TryExtractRoles_DelimitedStringUsesCustomDelimiter()
        {
            bool result = CustomJwtRoleClaimExtractor.TryExtractRoles(
                @"{ ""roles_csv"": "" admin, writer , ,auditor"" }",
                "roles_csv",
                JwtOptions.ROLES_FORMAT_DELIMITED_STRING,
                ",",
                NullLogger.Instance,
                out IReadOnlyList<string> roles);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "admin", "writer", "auditor" }, new List<string>(roles));
        }

        [TestMethod]
        public void TryExtractRoles_StringFormatDoesNotSplit()
        {
            bool result = CustomJwtRoleClaimExtractor.TryExtractRoles(
                @"{ ""role"": ""admin writer, auditor"" }",
                "role",
                JwtOptions.ROLES_FORMAT_STRING,
                NullLogger.Instance,
                out IReadOnlyList<string> roles);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "admin writer, auditor" }, new List<string>(roles));
        }

        [TestMethod]
        public void TryExtractRoles_MissingClaimLogsExpectedError()
        {
            Mock<ILogger> logger = new();

            bool result = CustomJwtRoleClaimExtractor.TryExtractRoles(
                @"{ ""groups"": [""admin""] }",
                "roles",
                JwtOptions.ROLES_FORMAT_ARRAY,
                logger.Object,
                out IReadOnlyList<string> roles);

            Assert.IsFalse(result);
            Assert.AreEqual(0, roles.Count);
            logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => object.Equals(o.ToString(), "Configured roles-path 'roles' was not found in JWT claims.")),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()),
                Times.Once);
        }

        [DataTestMethod]
        [DataRow("realm_access..roles")]
        [DataRow("['app.roles")]
        [DataRow("resource_access['dab-api'.roles")]
        [DataRow("groups[0]")]
        [DataRow("resource_access.clients[1].roles")]
        [DataRow("[]")]
        [DataRow("")]
        [DataRow("   ")]
        public void IsValidRolesPath_InvalidBracketSyntax_ReturnsFalse(string rolesPath)
        {
            Assert.IsFalse(CustomJwtRoleClaimExtractor.IsValidRolesPath(rolesPath));
        }

        [DataTestMethod]
        [DataRow("@env('JWT_ROLES_PATH')")]
        [DataRow("@akv('jwt-roles-path')")]
        public void IsValidRolesPath_VariableReference_ReturnsTrue(string rolesPath)
        {
            Assert.IsTrue(CustomJwtRoleClaimExtractor.IsValidRolesPath(rolesPath));
        }

        [DataTestMethod]
        [DataRow(@"{ ""roles"": [""admin"", 1] }", "roles", "array", DisplayName = "Array with non-string value")]
        [DataRow(@"{ ""roles"": { ""name"": ""admin"" } }", "roles", "array", DisplayName = "Object value")]
        [DataRow(@"{ ""roles"": 1 }", "roles", "array", DisplayName = "Number value")]
        [DataRow(@"{ ""roles"": true }", "roles", "array", DisplayName = "Boolean value")]
        [DataRow(@"{ ""roles"": null }", "roles", "array", DisplayName = "Null value")]
        [DataRow(@"{ ""groups"": [""admin""] }", "roles", "array", DisplayName = "Missing configured claim")]
        [DataRow(@"{ ""roles"": [""admin""] }", "roles", "string", DisplayName = "Wrong type for string format")]
        public void TryExtractRoles_Failure(string payloadJson, string rolesPath, string rolesFormat)
        {
            bool result = CustomJwtRoleClaimExtractor.TryExtractRoles(
                payloadJson,
                rolesPath,
                rolesFormat,
                NullLogger.Instance,
                out IReadOnlyList<string> roles);

            Assert.IsFalse(result);
            Assert.AreEqual(0, roles.Count);
        }
    }
}

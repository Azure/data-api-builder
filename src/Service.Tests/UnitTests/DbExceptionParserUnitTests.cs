// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit Test class for DbExceptionParserBase
    /// </summary>
    [TestClass]
    public class DbExceptionParserUnitTests
    {
        /// <summary>
        /// Verify that the DbExceptionParser returns the correct
        /// messaging based on the mode provided as argument.
        /// </summary>
        /// <param name="isDeveloperMode">true for developer mode, false otherwise.</param>
        /// <param name="expected">Expected error message.</param>
        [DataTestMethod]
        [DataRow(true, "Development Mode Error Message.")]
        [DataRow(false, "While processing your request the database ran into an error.")]
        public void VerifyCorrectErrorMessage(bool isDeveloperMode, string expected)
        {
            RuntimeConfigProvider provider = CreateMockConfigProvider(isDeveloperMode ? HostMode.Development : HostMode.Production);

            // Invoke base class implementation if no expectation overrides the member
            // (a.k.a. "Partial Mocks" in Rhino Mocks): default is false.
            // Without setting CallBase = true, the base class implementation of GetMessage(e) will
            // not be invoked and will return null.
            // https://github.com/devlooped/moq/wiki/Quickstart#customizing-mock-behavior
            Mock<DbExceptionParser> parser = new(provider) { CallBase = true };
            // 53 is a connection-establishment error; any code works for this message-only check.
            DbException e = SqlTestHelper.CreateSqlException(53, expected);
            Exception parsedException = parser.Object.Parse(e);
            Assert.AreEqual(expected: expected, actual: parsedException.Message);
        }

        /// <summary>
        /// Method to validate usage of the MsSqlDbExceptionParser.IsTransientException method.
        /// </summary>
        /// <param name="expected">boolean value indicating if exception is expected to be transient or not.</param>
        /// <param name="number">number to be populated in SqlException.Number field</param>
        [DataTestMethod]
        [DataRow(true, 121, DisplayName = "Transient exception error code #1")]
        [DataRow(true, 8628, DisplayName = "Transient exception error code #2")]
        [DataRow(true, 926, DisplayName = "Transient exception error code #3")]
        [DataRow(false, 107, DisplayName = "Non-transient exception error code #1")]
        [DataRow(false, 209, DisplayName = "Non-transient exception error code #2")]
        public void TestIsTransientExceptionMethod(bool expected, int number)
        {
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(CreateMockConfigProvider());

            Assert.AreEqual(expected, dbExceptionParser.IsTransientException(SqlTestHelper.CreateSqlException(number)));
        }

        /// <summary>
        /// Validates that client-input error codes - SQL Server 2025+ JSON validation errors (13608-13614)
        /// and XML parsing errors (9400-9480, e.g. 9413 unquoted attribute, 9436 end tag mismatch) - are
        /// mapped to HTTP 400 Bad Request and classified as client input errors.
        /// </summary>
        /// <param name="number">SQL error code populated in SqlException.Number.</param>
        [DataTestMethod]
        [DataRow(13608, DisplayName = "JSON validation error code 13608 maps to 400")]
        [DataRow(13609, DisplayName = "JSON validation error code 13609 maps to 400")]
        [DataRow(13610, DisplayName = "JSON validation error code 13610 maps to 400")]
        [DataRow(13611, DisplayName = "JSON validation error code 13611 maps to 400")]
        [DataRow(13612, DisplayName = "JSON validation error code 13612 maps to 400")]
        [DataRow(13613, DisplayName = "JSON validation error code 13613 maps to 400")]
        [DataRow(13614, DisplayName = "JSON validation error code 13614 maps to 400")]
        [DataRow(9400, DisplayName = "XML parsing error code 9400 (unexpected end of input) maps to 400")]
        [DataRow(9413, DisplayName = "XML parsing error code 9413 (string literal expected) maps to 400")]
        [DataRow(9436, DisplayName = "XML parsing error code 9436 (end tag mismatch) maps to 400")]
        [DataRow(9480, DisplayName = "XML parsing error code 9480 (unsupported xml) maps to 400")]
        [DataRow(6335, DisplayName = "XML data type error code 6335 maps to 400")]
        [DataRow(6359, DisplayName = "XML data type error code 6359 maps to 400")]
        [DataRow(6908, DisplayName = "XML data type error code 6908 maps to 400")]
        [DataRow(6926, DisplayName = "XML data type error code 6926 maps to 400")]
        [DataRow(305, DisplayName = "Unsupported xml comparison error code 305 maps to 400")]
        [DataRow(8116, DisplayName = "Invalid argument data type error code 8116 maps to 400")]
        [DataRow(8117, DisplayName = "Invalid operand data type error code 8117 maps to 400")]
        public void TestClientInputErrorsMapToBadRequest(int number)
        {
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(CreateMockConfigProvider());

            DbException sqlException = SqlTestHelper.CreateSqlException(number);

            Assert.AreEqual(
                System.Net.HttpStatusCode.BadRequest,
                dbExceptionParser.GetHttpStatusCodeForException(sqlException));
            Assert.AreEqual(
                DataApiBuilderException.SubStatusCodes.DatabaseInputError,
                dbExceptionParser.GetResultSubStatusCodeForException(sqlException));
        }

        /// <summary>
        /// Validates that XML input errors return a safe, XML-specific message in production mode.
        /// </summary>
        /// <param name="number">SQL error code populated in SqlException.Number.</param>
        [DataTestMethod]
        [DataRow(6335, DisplayName = "XML data type error code 6335 has a safe production message")]
        [DataRow(6359, DisplayName = "XML data type error code 6359 has a safe production message")]
        [DataRow(6908, DisplayName = "XML data type error code 6908 has a safe production message")]
        [DataRow(6926, DisplayName = "XML data type error code 6926 has a safe production message")]
        [DataRow(9413, DisplayName = "XML parsing error code 9413 has a safe production message")]
        [DataRow(9436, DisplayName = "XML parsing error code 9436 has a safe production message")]
        public void XmlErrorsHaveSafeProductionMessage(int number)
        {
            MsSqlDbExceptionParser parser = new(CreateMockConfigProvider(HostMode.Production));
            Assert.AreEqual("Invalid XML value supplied for an xml field.",
                parser.GetMessage(SqlTestHelper.CreateSqlException(number, "Sensitive database details")));
        }

        /// <summary>
        /// Validates that XML encoding error 9402 returns the encoding-declaration guidance, appended to
        /// the SQL Server message in development mode, and is classified as a client input error.
        /// </summary>
        /// <param name="hostMode">Host mode used to build the parser.</param>
        [DataTestMethod]
        [DataRow(HostMode.Production, DisplayName = "XML encoding error 9402 returns only guidance in production")]
        [DataRow(HostMode.Development, DisplayName = "XML encoding error 9402 appends guidance in development")]
        public void XmlEncodingErrorExplainsDeclarationFix(HostMode hostMode)
        {
            MsSqlDbExceptionParser parser = new(CreateMockConfigProvider(hostMode));
            DbException exception = SqlTestHelper.CreateSqlException(9402, "Sensitive database details");
            string guidance = "XML encoding declaration conflicts with UTF-16 input. Omit the XML encoding declaration or use encoding=\"utf-16\".";

            Assert.AreEqual(hostMode == HostMode.Development ? $"{exception.Message}. {guidance}" : guidance,
                parser.Parse(exception).Message);
            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, parser.GetHttpStatusCodeForException(exception));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabaseInputError,
                parser.GetResultSubStatusCodeForException(exception));
        }

        /// <summary>
        /// Builds a minimal MSSQL <see cref="RuntimeConfigProvider"/> backed by an in-memory config file.
        /// </summary>
        private static RuntimeConfigProvider CreateMockConfigProvider(HostMode hostMode = HostMode.Development)
        {
            RuntimeConfig mockConfig = new(
                Schema: "",
                DataSource: new(DatabaseType.MSSQL, "", new()),
                Runtime: new(Rest: new(), GraphQL: new(), Mcp: new(), Host: new(null, null, hostMode)),
                Entities: new(new Dictionary<string, Entity>()));
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            return new RuntimeConfigProvider(new FileSystemRuntimeConfigLoader(fileSystem));
        }
    }
}

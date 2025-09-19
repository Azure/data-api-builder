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
            RuntimeConfig mockConfig = new(
                Schema: "",
                DataSource: new(DatabaseType.MSSQL, "", new()),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null, isDeveloperMode ? HostMode.Development : HostMode.Production)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
            // We can use any other error code here, doesn't really matter.
            int connectionEstablishmentError = 53;

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);

            // Invoke base class implementation if no expectation overrides the member
            // (a.k.a. "Partial Mocks" in Rhino Mocks): default is false.
            // Without setting CallBase = true, the base class implementation of GetMessage(e) will
            // not be invoked and will return null.
            // https://github.com/devlooped/moq/wiki/Quickstart#customizing-mock-behavior
            Mock<DbExceptionParser> parser = new(provider) { CallBase = true };
            DbException e = SqlTestHelper.CreateSqlException(connectionEstablishmentError, expected);
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
            RuntimeConfig mockConfig = new(
                Schema: "",
                DataSource: new(DatabaseType.MSSQL, "", new()),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null, HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(provider);

            Assert.AreEqual(expected, dbExceptionParser.IsTransientException(SqlTestHelper.CreateSqlException(number)));
        }
    }
}

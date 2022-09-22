using System.Collections.Generic;
using System.Data.Common;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.Extensions.Logging;
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
            // We can use any other error code here, doesn't really matter.
            int connectionEstablishmentError = 53;
            Mock<RuntimeConfigPath> configPath = new();
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            Mock<RuntimeConfigProvider> provider = new(configPath.Object, configProviderLogger.Object);
            provider.Setup(x => x.IsDeveloperMode()).Returns(isDeveloperMode);
            Mock<DbExceptionParser> parser = new(provider.Object, new HashSet<string>());
            DbException e = SqlTestHelper.CreateSqlException(connectionEstablishmentError, expected);
            string actual = (parser.Object).Parse(e).Message;
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Method to validate usage of the DbExceptionParser.IsTransientException method.
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
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MSSQL);
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);

            Assert.AreEqual(expected, dbExceptionParser.IsTransientException(SqlTestHelper.CreateSqlException(number)));
        }
    }
}

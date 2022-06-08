using System.Data.Common;
using System.Reflection;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.Unittests
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
            Mock<IOptionsMonitor<RuntimeConfigPath>> runtimeConfigPath = new();
            runtimeConfigPath.Setup(x => x.CurrentValue.IsDeveloperMode()).Returns(isDeveloperMode);
            DbExceptionParserBase parser = new(runtimeConfigPath.Object);
            DbException e = CreateSqlException();
            string actual = parser.Parse(e).Message;
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Need to create our own DbException to invoke
        /// the parser, do that here and return. Use
        /// reflection to set _message for consistency and
        /// readability.
        /// </summary>
        /// <returns>DbException for use in our unit tests.</returns>
        private static DbException CreateSqlException()
        {
            SqlException e = null;
            try
            {
                SqlConnection connection = new(@"Data Source=NULL;Database=FAIL;Connection Timeout=1");
                connection.Open();
            }
            catch (SqlException ex)
            {
                // To keep a consistent and descriptive message
                // reflection is used to override "_message"
                typeof(SqlException)
                    .GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(ex, "Development Mode Error Message.");
                e = ex;
            }

            return e;
        }
    }
}

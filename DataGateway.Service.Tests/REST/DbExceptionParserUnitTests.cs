using Microsoft.VisualStudio.TestTools.UnitTesting;
using Azure.DataGateway.Service.Resolvers;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Azure.DataGateway.Config;
using System.Reflection;

namespace Azure.DataGateway.Service.Tests.REST
{
    /// <summary>
    /// Unit Test class for DbExceptionParserBase
    /// </summary>
    [TestClass]
    public class DbExceptionParserUnitTests
    {
        /// <summary>
        /// Verify that the DbExceptionParser returns the correct
        /// messaging based on the mode sprovided as argument
        /// </summary>
        /// <param name="mode">Production or Developer.</param>
        /// <param name="expected">Expected error message.</param>
        [DataTestMethod]
        [DataRow(HostModeType.Development, "While processing your request the database ran into an error.")]
        [DataRow(HostModeType.Production, "Production Error Message.")]
        public void VerifyCorrectErrorMessage(HostModeType mode, string expected)
        {
            DbExceptionParserBase parser = new();
            DbException e = CreateSqlException();
            string actual = parser.Parse(e, mode).Message;
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
                    .SetValue(ex, "Production Error Message.");
                e = ex;
            }

            return e;
        }
    }
}

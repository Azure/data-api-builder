using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Units testing for our connection string parser
    /// to retreive schema.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class SqlMetadataProviderUnitTests
    {
        /// <summary>
        /// Verify we parse the connection string for the
        /// schema correctly when it is at the end
        /// of the connection string and is empty.
        /// </summary>
        [TestMethod]
        public void CheckConnectionStringEmptySearchPathLastTest()
        {
            string expected = string.Empty;
            string actual;
            string connectionString = "Host=localhost;Database=graphql;SearchPath=";

            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }

        /// <summary>
        /// Verify we parse the connection string for the
        /// schema correctly when it is at the end
        /// of the connection string and is normal.
        /// </summary>
        [TestMethod]
        public void CheckConnectionStringSchemaInSearchPathTest()
        {
            string expected = "foobar";
            string actual;
            string connectionString = "Host=localhost;Database=graphql;SearchPath=foobar";

            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }

        /// <summary>
        /// Verify we parse the connection string for the
        /// schema correctly when it is first in the connection
        /// string and is normal.
        /// </summary>
        [TestMethod]
        public void CheckConnectionStringSchemaInSearchPathFirstTest()
        {
            string expected = "baz";
            string actual;
            string connectionString = "SearchPath=\"baz\";Host=localhost;Database=graphql";

            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }

        /// <summary>
        /// Verify we parse the connection string for the schema
        /// correctly when SearchPath does not exist in the connection
        /// string.
        /// </summary>
        [TestMethod]
        public void CheckConnectionStringNoSchemaInSearchPathTest()
        {
            string expected = string.Empty;
            string actual;
            string connectionString = "Host=localhost;Database=graphql";

            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }

        /// <summary>
        /// Verify we parse the connection string for the schema
        /// correctly when SearchPath is first in the connection
        /// string and empty.
        /// </summary>
        [TestMethod]
        public void CheckConnectionStringEmptySearchPathFirstTest()
        {
            string expected = string.Empty;
            string actual;
            string connectionString = "SearchPath=;Host=localhost;Database=graphql";

            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }
    }
}

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
        [DataTestMethod]
        [DataRow("", "", "Host=localhost;Database=graphql;SearchPath=\"\"")]
        [DataRow("", "", "Host=localhost;Database=graphql;SearchPath=")]
        public void CheckConnectionStringEmptySearchPathLastTest(string expected, string actual, string connectionString)
        {
            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }

        /// <summary>
        /// Verify we parse the connection string for the
        /// schema correctly when it is at the end
        /// of the connection string and is normal.
        /// </summary>
        [DataTestMethod]
        [DataRow("foobar", "", "Host=localhost;Database=graphql;SearchPath=foobar")]
        [DataRow("foobar", "", "Host=localhost;Database=graphql;SearchPath=\"foobar\"")]
        public void CheckConnectionStringSchemaInSearchPathTest(string expected, string actual, string connectionString)
        {
            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }

        /// <summary>
        /// Verify we parse the connection string for the
        /// schema correctly when it is first in the connection
        /// string and is normal.
        /// </summary>
        [DataTestMethod]
        [DataRow("baz", "", "SearchPath=\"baz\";Host=localhost;Database=graphql")]
        [DataRow("baz", "", "SearchPath=baz;Host=localhost;Database=graphql")]
        public void CheckConnectionStringSchemaInSearchPathFirstTest(string expected, string actual, string connectionString)
        {
            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }

        /// <summary>
        /// Verify we parse the connection string for the schema
        /// correctly when SearchPath does not exist in the connection
        /// string.
        /// </summary>
        [DataTestMethod]
        [DataRow("", "", "Host=localhost;Database=graphql")]
        public void CheckConnectionStringNoSchemaInSearchPathTest(string expected, string actual, string connectionString)
        {
            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }

        /// <summary>
        /// Verify we parse the connection string for the schema
        /// correctly when SearchPath is first in the connection
        /// string and empty.
        /// </summary>
        [DataTestMethod]
        [DataRow("", "", "SearchPath=;Host=localhost;Database=graphql")]
        [DataRow("", "", "SearchPath=\"\";Host=localhost;Database=graphql")]
        public void CheckConnectionStringEmptySearchPathFirstTest(string expected, string actual, string connectionString)
        {
            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out actual, connectionString);
            Assert.IsTrue(expected.Equals(actual));
        }
    }
}

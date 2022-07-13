using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.UnitTests
{
    /// <summary>
    /// Units testing for our connection string parser
    /// to retreive schema.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class SqlMetadataProviderUnitTests : SqlTestBase
    {
        /// <summary>
        /// Verify we parse the connection string for the
        /// schema correctly when it is of various relevant
        /// formats.
        /// </summary>
        [DataTestMethod]
        [DataRow("", "Host=localhost;Database=graphql;SearchPath=\"\"")]
        [DataRow("", "Host=localhost;Database=graphql;SearchPath=")]
        [DataRow("foobar", "Host=localhost;Database=graphql;SearchPath=foobar")]
        [DataRow("foobar", "Host=localhost;Database=graphql;SearchPath=\"foobar\"")]
        [DataRow("baz", "SearchPath=\"baz\";Host=localhost;Database=graphql")]
        [DataRow("baz", "SearchPath=baz;Host=localhost;Database=graphql")]
        [DataRow("", "Host=localhost;Database=graphql")]
        [DataRow("", "SearchPath=;Host=localhost;Database=graphql")]
        [DataRow("", "SearchPath=\"\";Host=localhost;Database=graphql")]
        public void CheckConnectionStringParsingTest(string expected, string connectionString)
        {
            PostgreSqlMetadataProvider.TryGetSchemaFromConnectionString(out string actual, connectionString);
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// <code>Do: </code> Fills the table definition with information of the foreign keys
        /// for all the tables based on the entities relationship.
        /// <code>Check: </code> Making sure no exception is thrown if there are no Foriegn Keys.
        /// </summary>
        [TestMethod]
        public async Task CheckNoExceptionForNoForiegnKey()
        {
            RuntimeConfigPath configPath = TestHelper.GetRuntimeConfigPath(TestCategory.POSTGRESQL);
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider.ConfigProviderLogger = configProviderLogger.Object;
            RuntimeConfigProvider.LoadRuntimeConfigValue(configPath, out _runtimeConfig);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(_runtimeConfig);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            SetUpSQLMetadataProvider();
            await ResetDbStateAsync();
            await _sqlMetadataProvider.InitializeAsync();
        }
    }
}

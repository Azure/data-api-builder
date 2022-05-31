using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Units testing for our connection string parser
    /// to retreive schema.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class SqlMetadataProviderUnitTests : GraphQLMutationTestBase
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
            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out string actual, connectionString);
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// <code>Do: </code> Fills the table definition with information of the foreign keys
        /// for all the tables based on the entities in runtimeConfig file.
        /// <code>Check: </code> Making sure no exception is thrown if there are no Foriegn Keys.
        /// <code>Note: </code> This test is independent of DB, so any DB(POSTGRES,MSSQL,MYSQL) can be used.
        /// </summary>
        [TestMethod]
        public async Task CheckNoExceptionForNoForiegnKey()
        {
            string customRuntimeTestConfig = "hawaii-config-test.PostgreSql.NoFk.json";
            
            string originalJsonString = File.ReadAllText(RuntimeConfigPath.GetFileNameForEnvironment(TestCategory.POSTGRESQL));
            JsonSerializerOptions options = RuntimeConfig.GetDeserializationOptions();

            RuntimeConfig originalRuntimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(originalJsonString, options);

            string customJsonString = File.ReadAllText(customRuntimeTestConfig);

            RuntimeConfig customRuntimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(customJsonString, options);
            customRuntimeConfig.ConnectionString = originalRuntimeConfig.ConnectionString;
            string updatedJson = JsonSerializer.Serialize(customRuntimeConfig, options);
            File.WriteAllText(customRuntimeTestConfig, updatedJson);

            IQueryBuilder queryBuilder = new PostgresQueryBuilder();
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath = SqlTestHelper.LoadCustomConfig(customRuntimeTestConfig);
            DbExceptionParserBase dbExceptionParser = new PostgresDbExceptionParser();
            IQueryExecutor queryExecutor = new QueryExecutor<NpgsqlConnection>(runtimeConfigPath, dbExceptionParser);
            PostgreSqlMetadataProvider sqlMetadataProvider = new(runtimeConfigPath, queryExecutor, queryBuilder);

            Console.WriteLine("Custom Config file set successful.");
            try
            {
                await sqlMetadataProvider.InitializeAsync();
            }
            catch (Exception ex)
            {
                Assert.Fail("Expected no exception, but got: " + ex.Message);
            }
        }
    }
}

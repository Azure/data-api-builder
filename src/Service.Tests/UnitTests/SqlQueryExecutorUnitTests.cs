// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class SqlQueryExecutorUnitTests
    {
        // Error code for semaphore timeout in MsSql.
        private const int ERRORCODE_SEMAPHORE_TIMEOUT = 121;

        // The key of the item stored in http context
        private const string TOTAL_DB_EXECUTION_TIME = "TotalDbExecutionTime";

        /// <summary>
        /// Validates managed identity token issued ONLY when connection string does not specify
        /// User, Password, and Authentication method.
        /// </summary>
        [DataTestMethod]
        [DataRow("Server =<>;Database=<>;User=xyz;", false, false,
            DisplayName = "No managed identity access token when connection string specifies User only.")]
        [DataRow("Server =<>;Database=<>;Password=xyz;", false, false,
            DisplayName = "No managed identity access token when connection string specifies Password only.")]
        [DataRow("Server =<>;Database=<>;Authentication=Active Directory Integrated;", false, false,
            DisplayName = "No managed identity access token when connection string specifies Authentication method only.")]
        [DataRow("Server =<>;Database=<>;User=xyz;Password=xxx", false, false,
            DisplayName = "No managed identity access token when connection string specifies both User and Password.")]
        [DataRow("Server =<>;Database=<>;UID=xyz;Pwd=xxx", false, false,
            DisplayName = "No managed identity access token when connection string specifies Uid and Pwd.")]
        [DataRow("Server =<>;Database=<>;User=xyz;Authentication=Active Directory Service Principal", false, false,
            DisplayName = "No managed identity access when connection string specifies both User and Authentication method.")]
        [DataRow("Server =<>;Database=<>;Password=xxx;Authentication=Active Directory Password;", false, false,
            DisplayName = "No managed identity access token when connection string specifies both Password and Authentication method.")]
        [DataRow("Server =<>;Database=<>;User=xyz;Password=xxx;Authentication=SqlPassword", false, false,
            DisplayName = "No managed identity access token when connection string specifies User, Password and Authentication method.")]
        [DataRow("Server =<>;Database=<>;Trusted_Connection=yes", false, false,
            DisplayName = "No managed identity access token when connection string specifies Trusted Connection.")]
        [DataRow("Server =<>;Database=<>;Integrated Security=true", false, false,
            DisplayName = "No managed identity access token when connection string specifies Integrated Security.")]
        [DataRow("Server =<>;Database=<>;", true, false,
            DisplayName = "Managed identity access token from config used " +
                "when connection string specifies none of User, Password and Authentication method.")]
        [DataRow("Server =<>;Database=<>;", true, true,
            DisplayName = "Default managed identity access token used " +
                "when connection string specifies none of User, Password and Authentication method")]
        public async Task TestHandleManagedIdentityAccess(
            string connectionString,
            bool expectManagedIdentityAccessToken,
            bool isDefaultAzureCredential)
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MSSQL, connectionString, new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
           );

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            Mock<DbExceptionParser> dbExceptionParser = new(provider);
            Mock<ILogger<MsSqlQueryExecutor>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            MsSqlQueryExecutor msSqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);

            const string DEFAULT_TOKEN = "Default access token";
            const string CONFIG_TOKEN = "Configuration controller access token";
            AccessToken testValidToken = new(accessToken: DEFAULT_TOKEN, expiresOn: DateTimeOffset.MaxValue);
            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Mock<DefaultAzureCredential> dacMock = new();
                    dacMock
                        .Setup(m => m.GetTokenAsync(It.IsAny<TokenRequestContext>(),
                            It.IsAny<System.Threading.CancellationToken>()))
                        .Returns(ValueTask.FromResult(testValidToken));
                    msSqlQueryExecutor.AzureCredential = dacMock.Object;
                }
                else
                {
                    await provider.Initialize(
                        provider.GetConfig().ToJson(),
                        graphQLSchema: null,
                        connectionString: connectionString,
                        accessToken: CONFIG_TOKEN,
                        replacementSettings: new());
                    msSqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);
                }
            }

            using SqlConnection conn = new(connectionString);
            await msSqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, string.Empty);

            if (expectManagedIdentityAccessToken)
            {
                if (isDefaultAzureCredential)
                {
                    Assert.AreEqual(expected: DEFAULT_TOKEN, actual: conn.AccessToken);
                }
                else
                {
                    Assert.AreEqual(expected: CONFIG_TOKEN, actual: conn.AccessToken);
                }
            }
            else
            {
                Assert.AreEqual(expected: default, actual: conn.AccessToken);
            }
        }

        /// <summary>
        /// Test to validate that when a query successfully executes within the allowed number of retries, a result is returned
        /// and no further retries occur.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestRetryPolicyExhaustingMaxAttempts()
        {
            int maxRetries = 2;
            int maxAttempts = maxRetries + 1; // 1 represents the original attempt to execute the query in addition to retries.
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MSSQL, "", new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
           );

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader)
            {
                IsLateConfigured = true
            };

            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(provider);
            Mock<MsSqlQueryExecutor> queryExecutor
                = new(provider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object, null, null);

            queryExecutor.Setup(x => x.ConnectionStringBuilders).Returns(new Dictionary<string, DbConnectionStringBuilder>());

            queryExecutor.Setup(x => x.CreateConnection(
               It.IsAny<string>())).CallBase();

            // Mock the ExecuteQueryAgainstDbAsync to throw a transient exception.
            queryExecutor.Setup(x => x.ExecuteQueryAgainstDbAsync(
                It.IsAny<SqlConnection>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
                It.IsAny<HttpContext>(),
                provider.GetConfig().DefaultDataSourceName,
                It.IsAny<List<string>>()))
            .Throws(SqlTestHelper.CreateSqlException(ERRORCODE_SEMAPHORE_TIMEOUT));

            // Call the actual ExecuteQueryAsync method.
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
                It.IsAny<string>(),
                It.IsAny<HttpContext>(),
                It.IsAny<List<string>>())).CallBase();

            DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(async () =>
            {
                await queryExecutor.Object.ExecuteQueryAsync<object>(
                    sqltext: string.Empty,
                    parameters: new Dictionary<string, DbConnectionParam>(),
                    dataReaderHandler: null,
                    dataSourceName: String.Empty,
                    httpContext: null,
                    args: null);
            });

            Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);

            // For each attempt logger is invoked once. Currently we have hardcoded the number of attempts.
            // Once we have number of retry attempts specified in config, we will make it dynamic.
            Assert.AreEqual(maxAttempts, queryExecutorLogger.Invocations.Count);
        }

        /// <summary>
        /// Test to validate that DbCommand parameters are correctly populated with the provided values and database types.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public void Test_DbCommandParameter_PopulatedWithCorrectDbTypes()
        {
            // Setup mock configuration
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MSSQL, "Server =<>;Database=<>;", new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
           );

            // Setup file system and loader for runtime configuration
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);

            // Setup necessary mocks and objects for MsSqlQueryExecutor
            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(provider);

            // Instantiate the MsSqlQueryExecutor and Setup parameters for the query
            MsSqlQueryExecutor msSqlQueryExecutor = new(provider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object);
            IDictionary<string, DbConnectionParam> parameters = new Dictionary<string, DbConnectionParam>();
            parameters.Add("@param1", new DbConnectionParam("My Awesome book", DbType.AnsiString, SqlDbType.VarChar));
            parameters.Add("@param2", new DbConnectionParam("Ramen", DbType.String, SqlDbType.NVarChar));

            // Prepare the DbCommand
            DbCommand dbCommand = msSqlQueryExecutor.PrepareDbCommand(new SqlConnection(), "SELECT * FROM books where title='My Awesome book' and author='Ramen'", parameters, null, null);

            // Assert that the parameters are correctly populated with the provided values and database types.
            List<SqlParameter> parametersList = dbCommand.Parameters.OfType<SqlParameter>().ToList();
            Assert.AreEqual(parametersList[0].Value, "My Awesome book");
            Assert.AreEqual(parametersList[0].DbType, DbType.AnsiString);
            Assert.AreEqual(parametersList[0].SqlDbType, SqlDbType.VarChar);
            Assert.AreEqual(parametersList[1].Value, "Ramen");
            Assert.AreEqual(parametersList[1].DbType, DbType.String);
            Assert.AreEqual(parametersList[1].SqlDbType, SqlDbType.NVarChar);
        }

        /// <summary>
        /// Validates that a query successfully executes within two retries by checking that the SqlQueryExecutor logger
        /// was invoked the expected number of times.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestRetryPolicySuccessfullyExecutingQueryAfterNAttempts()
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader) { IsLateConfigured = true };
            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(provider);
            EventHandler handler = null;
            IOboTokenProvider oboTokenProvider = null;
            Mock<MsSqlQueryExecutor> queryExecutor
                = new(provider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object, handler, oboTokenProvider);

            queryExecutor.Setup(x => x.ConnectionStringBuilders).Returns(new Dictionary<string, DbConnectionStringBuilder>());

            queryExecutor.Setup(x => x.CreateConnection(
               It.IsAny<string>())).CallBase();

            queryExecutor.Setup(x => x.PrepareDbCommand(
                It.IsAny<SqlConnection>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<string>())).CallBase();

            // Mock the ExecuteQueryAgainstDbAsync to throw a transient exception.
            queryExecutor.SetupSequence(x => x.ExecuteQueryAgainstDbAsync(
                It.IsAny<SqlConnection>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
                It.IsAny<HttpContext>(),
                provider.GetConfig().DefaultDataSourceName,
                It.IsAny<List<string>>()))
            .Throws(SqlTestHelper.CreateSqlException(ERRORCODE_SEMAPHORE_TIMEOUT))
            .Throws(SqlTestHelper.CreateSqlException(ERRORCODE_SEMAPHORE_TIMEOUT))
            .CallBase();

            // Call the actual ExecuteQueryAsync method.
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
                It.IsAny<string>(),
                It.IsAny<HttpContext>(),
                It.IsAny<List<string>>())).CallBase();

            string sqltext = "SELECT * from books";

            await queryExecutor.Object.ExecuteQueryAsync<object>(
                    sqltext: sqltext,
                    dataSourceName: String.Empty,
                    parameters: new Dictionary<string, DbConnectionParam>(),
                    dataReaderHandler: null,
                    args: null);

            // The logger is invoked three (3) times, once for each of the following events:
            // The query fails on the first attempt (log event 1).
            // The query fails on the second attempt/first retry (log event 2).
            // The query succeeds on the third attempt/second retry (log event 3).
            Assert.AreEqual(3, queryExecutorLogger.Invocations.Count);
        }

        /// <summary>
        /// Test to validate that when a query is executed the httpcontext object is populated with the time it took to run the query.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestHttpContextIsPopulatedWithDbExecutionTime()
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MSSQL, "", new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
           );

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader)
            {
                IsLateConfigured = true
            };

            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            HttpContext context = new DefaultHttpContext();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(provider);
            Mock<MsSqlQueryExecutor> queryExecutor
                = new(provider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object, null, null);

            queryExecutor.Setup(x => x.ConnectionStringBuilders).Returns(new Dictionary<string, DbConnectionStringBuilder>());

            // Call the actual ExecuteQueryAsync method.
            queryExecutor.Setup(x => x.ExecuteQueryAgainstDbAsync(
                It.IsAny<SqlConnection>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>, Task<object>>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>())).CallBase();

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                await queryExecutor.Object.ExecuteQueryAgainstDbAsync<object>(
                    conn: null,
                    sqltext: string.Empty,
                    parameters: new Dictionary<string, DbConnectionParam>(),
                    dataReaderHandler: null,
                    dataSourceName: String.Empty,
                    httpContext: null,
                    args: null);
            }
            catch (Exception)
            {
                // as the SqlConnection object is a sealed class and can't be mocked, ignore any exceptions caused to bypass
            }

            stopwatch.Stop();

            Assert.IsTrue(context.Items.ContainsKey(TOTAL_DB_EXECUTION_TIME), "HttpContext object must contain the total db execution time after execution of a query");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds >= (long)context.Items[TOTAL_DB_EXECUTION_TIME], "The execution time stored in http context must be valid.");
        }

        /// <summary>
        /// Test to validate whether we are adding an info message handler when adding the connection
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public void TestInfoMessageHandlerIsAdded()
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader) { IsLateConfigured = true };
            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(provider);
            EventHandler handler = null;
            IOboTokenProvider oboTokenProvider = null;
            Mock<MsSqlQueryExecutor> queryExecutor
                = new(provider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object, handler, oboTokenProvider);

            queryExecutor.Setup(x => x.ConnectionStringBuilders).Returns(new Dictionary<string, DbConnectionStringBuilder>());

            // Call the actual CreateConnection method.
            queryExecutor.Setup(x => x.CreateConnection(
                It.IsAny<string>())).CallBase();

            SqlConnection conn = queryExecutor.Object.CreateConnection(provider.GetConfig().DefaultDataSourceName);

            FieldInfo eventField = typeof(SqlConnection).GetField("InfoMessage", BindingFlags.NonPublic | BindingFlags.Instance);

            MulticastDelegate eventDelegate = (MulticastDelegate)eventField.GetValue(conn);

            Delegate[] handlers = eventDelegate.GetInvocationList();

            Assert.IsTrue(handlers.Length != 0);
        }

        /// <summary>
        /// Test to validate that when a query is executed the httpcontext object is updated with time correctly when multiple threads are accessing it.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public void TestToValidateLockingOfHttpContextObjectDuringCalcuationOfDbExecutionTime()
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MSSQL, "", new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
           );

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader)
            {
                IsLateConfigured = true
            };

            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            HttpContext context = new DefaultHttpContext();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(provider);
            MsSqlQueryExecutor queryExecutor = new(provider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object, null);

            long timeToAdd = 50L;
            int threadCount = 10;  // Simulate multiple threads
            Task[] tasks = new Task[threadCount];

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    queryExecutor.AddDbExecutionTimeToMiddlewareContext(timeToAdd);
                });
            }

            Task.WaitAll(tasks);

            // Assert
            Assert.IsTrue(context.Items.ContainsKey("TotalDbExecutionTime"), "HttpContext object must contain the total db execution time after execution of a query");

            Assert.AreEqual(50L * threadCount, context.Items["TotalDbExecutionTime"], "With 10 threads adding 50 to the total db execution time, context.items[TotalDbExecutionTime] must be 500");
        }

        /// <summary>
        /// Validates streaming logic for QueryExecutor
        /// In this test the DbDataReader.GetChars method is mocked to return 1024*1024 bytes (1MB) of data.
        /// Max available size is set to 5 MB.
        /// Based on number of loops, the data read will be 1MB * readDataLoops.Exception should be thrown in test cases where we go above 5MB.
        /// This will be in cases where readDataLoops > 5.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow(4, false,
            DisplayName = "Max available size is set to 5MB.4 data read loop iterations * 1MB -> should successfully read 4MB because max-db-response-size-mb is 4MB")]
        [DataRow(5, false,
            DisplayName = "Max available size is set to 5MB.5 data read loop iterations * 1MB -> should successfully read 5MB because max-db-response-size-mb is 5MB")]
        [DataRow(6, true,
            DisplayName = "Max available size is set to 5MB.6 data read loop iterations * 1MB -> Fails to read 6MB because max-db-response-size-mb is 5MB")]
        public void ValidateStreamingLogicAsync(int readDataLoops, bool exceptionExpected)
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                        Rest: new(),
                        GraphQL: new(),
                        Mcp: new(),
                        Host: new(Cors: null, Authentication: null, MaxResponseSizeMB: 5)
                    ),
                Entities: new(new Dictionary<string, Entity>()));

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);

            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);

            // Instantiate the MsSqlQueryExecutor and Setup parameters for the query
            MsSqlQueryExecutor msSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object);

            try
            {
                Mock<DbDataReader> dbDataReader = new();
                dbDataReader.Setup(d => d.HasRows).Returns(true);
                dbDataReader.Setup(x => x.GetChars(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<char[]>(), It.IsAny<int>(), It.IsAny<int>())).Returns(1024 * 1024);
                int availableSize = (int)runtimeConfig.MaxResponseSizeMB() * 1024 * 1024;
                for (int i = 0; i < readDataLoops; i++)
                {
                    availableSize -= msSqlQueryExecutor.StreamCharData(
                        dbDataReader: dbDataReader.Object, availableSize: availableSize, resultJsonString: new(), ordinal: 0);
                }

            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(exceptionExpected);
                Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, ex.StatusCode);
                Assert.AreEqual("The JSON result size exceeds max result size of 5MB. Please use pagination to reduce size of result.", ex.Message);
            }
        }

        /// <summary>
        /// Validates streaming logic for QueryExecutor
        /// In this test the streaming logic for stored procedures is tested.
        /// The test tries to validate the streaming across different column types (Byte, string, int etc)
        /// Max available size is set to 4 MB, getChars and getBytes are moqed to return 1MB per read.
        /// Exception should be thrown in test cases where we go above 4MB.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow(4, false,
            DisplayName = "Max available size is set to 4MB.4 data read loop iterations, 4 columns of size 1MB -> should successfully read because max-db-response-size-mb is 4MB")]
        [DataRow(5, true,
            DisplayName = "Max available size is set to 4MB.5 data read loop iterations, 4 columns of size 1MB and one int read of 4 bytes -> Fails to read because max-db-response-size-mb is 4MB")]
        public void ValidateStreamingLogicForStoredProcedures(int readDataLoops, bool exceptionExpected)
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            string[] columnNames = { "NVarcharStringColumn1", "VarCharStringColumn2", "ImageByteColumn", "ImageByteColumn2", "IntColumn" };
            // 1MB -> 1024*1024 bytes, an int is 4 bytes
            int[] columnSizeBytes = { 1024 * 1024, 1024 * 1024, 1024 * 1024, 1024 * 1024, 4 };

            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                        Rest: new(),
                        GraphQL: new(),
                        Mcp: new(),
                        Host: new(Cors: null, Authentication: null, MaxResponseSizeMB: 4)
                    ),
                Entities: new(new Dictionary<string, Entity>()));

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);

            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);

            // Instantiate the MsSqlQueryExecutor and Setup parameters for the query
            MsSqlQueryExecutor msSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object);

            try
            {
                // Test for general queries and mutations
                Mock<DbDataReader> dbDataReader = new();
                dbDataReader.Setup(d => d.HasRows).Returns(true);
                dbDataReader.Setup(x => x.GetChars(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<char[]>(), It.IsAny<int>(), It.IsAny<int>())).Returns(1024 * 1024);
                dbDataReader.Setup(x => x.GetBytes(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>())).Returns(1024 * 1024);
                dbDataReader.Setup(x => x.GetFieldType(0)).Returns(typeof(string));
                dbDataReader.Setup(x => x.GetFieldType(1)).Returns(typeof(string));
                dbDataReader.Setup(x => x.GetFieldType(2)).Returns(typeof(byte[]));
                dbDataReader.Setup(x => x.GetFieldType(3)).Returns(typeof(byte[]));
                dbDataReader.Setup(x => x.GetFieldType(4)).Returns(typeof(int));
                int availableSizeBytes = runtimeConfig.MaxResponseSizeMB() * 1024 * 1024;
                DbResultSetRow dbResultSetRow = new();
                for (int i = 0; i < readDataLoops; i++)
                {
                    availableSizeBytes -= msSqlQueryExecutor.StreamDataIntoDbResultSetRow(
                        dbDataReader.Object, dbResultSetRow, columnName: columnNames[i],
                        columnSize: columnSizeBytes[i], ordinal: i, availableBytes: availableSizeBytes);
                    Assert.IsTrue(dbResultSetRow.Columns.ContainsKey(columnNames[i]), $"Column {columnNames[i]} should be successfully read and added to DbResultRow while streaming.");
                }
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(exceptionExpected);
                Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, ex.StatusCode);
                Assert.AreEqual("The JSON result size exceeds max result size of 4MB. Please use pagination to reduce size of result.", ex.Message);
            }
        }

        /// <summary>
        /// Makes sure the stream logic handles cells with empty strings correctly.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        public void ValidateStreamingLogicForEmptyCellsAsync()
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, "", Options: null),
                Runtime: new(
                        Rest: new(),
                        GraphQL: new(),
                        Mcp: new(),
                        Host: new(Cors: null, Authentication: null, MaxResponseSizeMB: 5)
                    ),
                Entities: new(new Dictionary<string, Entity>()));

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);

            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);

            // Instantiate the MsSqlQueryExecutor and Setup parameters for the query
            MsSqlQueryExecutor msSqlQueryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object);

            Mock<DbDataReader> dbDataReader = new();
            dbDataReader.Setup(d => d.HasRows).Returns(true);

            // Make sure GetChars returns 0 when buffer is null
            dbDataReader.Setup(x => x.GetChars(It.IsAny<int>(), It.IsAny<long>(), null, It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            // Make sure available size is set to > 0
            int availableSize = (int)runtimeConfig.MaxResponseSizeMB() * 1024 * 1024;

            // Stream char data should not return an exception
            availableSize -= msSqlQueryExecutor.StreamCharData(
                dbDataReader: dbDataReader.Object, availableSize: availableSize, resultJsonString: new(), ordinal: 0);

            Assert.AreEqual(availableSize, (int)runtimeConfig.MaxResponseSizeMB() * 1024 * 1024);
        }

        #region Per-User Connection Pooling Tests

        /// <summary>
        /// Creates MsSqlQueryExecutor with the specified configuration for per-user connection pooling tests.
        /// </summary>
        /// <param name="connectionString">The connection string to use.</param>
        /// <param name="enableObo">Whether to enable user-delegated-auth (OBO).</param>
        /// <param name="httpContextAccessor">The HttpContextAccessor mock to use.</param>
        /// <returns>A tuple containing the query executor and runtime config provider.</returns>
        private static (MsSqlQueryExecutor QueryExecutor, RuntimeConfigProvider Provider) CreateQueryExecutorForPoolingTest(
            string connectionString,
            bool enableObo,
            Mock<IHttpContextAccessor> httpContextAccessor)
        {
            DataSource dataSource = new(
                DatabaseType: DatabaseType.MSSQL,
                ConnectionString: connectionString,
                Options: null)
            {
                UserDelegatedAuth = enableObo
                    ? new UserDelegatedAuthOptions(
                        Enabled: true,
                        Provider: "EntraId",
                        DatabaseAudience: "https://database.windows.net")
                    : null
            };

            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: dataSource,
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>()));

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);

            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(provider);

            MsSqlQueryExecutor queryExecutor = new(provider, dbExceptionParser, queryExecutorLogger.Object, httpContextAccessor.Object);
            return (queryExecutor, provider);
        }

        /// <summary>
        /// Creates an HttpContextAccessor mock with the specified user claims.
        /// </summary>
        /// <param name="issuer">The issuer claim value, or empty string for no context.</param>
        /// <param name="objectId">The oid claim value, or empty string for no context.</param>
        /// <returns>A configured HttpContextAccessor mock.</returns>
        private static Mock<IHttpContextAccessor> CreateHttpContextAccessorWithClaims(string issuer, string objectId)
        {
            Mock<IHttpContextAccessor> httpContextAccessor = new();

            if (string.IsNullOrEmpty(issuer) && string.IsNullOrEmpty(objectId))
            {
                httpContextAccessor.Setup(x => x.HttpContext).Returns(value: null);
            }
            else
            {
                DefaultHttpContext context = new();
                System.Security.Claims.ClaimsIdentity identity = new("TestAuth");
                if (!string.IsNullOrEmpty(issuer))
                {
                    identity.AddClaim(new System.Security.Claims.Claim("iss", issuer));
                }

                if (!string.IsNullOrEmpty(objectId))
                {
                    identity.AddClaim(new System.Security.Claims.Claim("oid", objectId));
                }

                context.User = new System.Security.Claims.ClaimsPrincipal(identity);
                httpContextAccessor.Setup(x => x.HttpContext).Returns(context);
            }

            return httpContextAccessor;
        }

        /// <summary>
        /// Test that pooling remains enabled regardless of whether user-delegated-auth is configured.
        /// When OBO is enabled, per-user pooling is automatic. When disabled, pooling stays as configured.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow(true, DisplayName = "OBO enabled - per-user pooling is automatic")]
        [DataRow(false, DisplayName = "OBO disabled - pooling not modified")]
        public void TestPoolingBehaviorWithAndWithoutUserDelegatedAuth(bool enableUserDelegatedAuth)
        {
            // Arrange & Act
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            (MsSqlQueryExecutor queryExecutor, RuntimeConfigProvider provider) = CreateQueryExecutorForPoolingTest(
                connectionString: "Server=localhost;Database=test;Pooling=true;",
                enableObo: enableUserDelegatedAuth,
                httpContextAccessor: httpContextAccessor);

            SqlConnectionStringBuilder connBuilder = new(queryExecutor.ConnectionStringBuilders[provider.GetConfig().DefaultDataSourceName].ConnectionString);

            // Assert - pooling should be enabled in both cases
            string expectedMessage = enableUserDelegatedAuth
                ? "Pooling should be enabled when user-delegated-auth is configured (per-user pooling is automatic)"
                : "Pooling should remain as configured when user-delegated-auth is not used";
            Assert.IsTrue(connBuilder.Pooling, expectedMessage);
        }

        /// <summary>
        /// Test that when OBO is enabled, connection pooling is forcibly enabled even if
        /// the original connection string has Pooling=false. Per-user pooling is required
        /// for OBO to prevent connection exhaustion, so DAB intentionally overrides this setting.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public void TestOboEnabled_ForciblyEnablesPooling_EvenWhenConnectionStringDisablesIt()
        {
            // Arrange - connection string explicitly disables pooling
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            (MsSqlQueryExecutor queryExecutor, RuntimeConfigProvider provider) = CreateQueryExecutorForPoolingTest(
                connectionString: "Server=localhost;Database=test;Pooling=false;",
                enableObo: true,
                httpContextAccessor: httpContextAccessor);

            // Act - check the stored connection string builder
            SqlConnectionStringBuilder connBuilder = new(
                queryExecutor.ConnectionStringBuilders[provider.GetConfig().DefaultDataSourceName].ConnectionString);

            // Assert - pooling should be forcibly enabled despite Pooling=false in original connection string
            Assert.IsTrue(connBuilder.Pooling,
                "OBO requires per-user pooling, so Pooling should be forcibly enabled even when connection string specifies Pooling=false");
        }

        /// <summary>
        /// Test that when OBO is enabled and user claims are present, CreateConnection returns
        /// a connection string with a user-specific Application Name containing the pool hash.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public void TestOboWithUserClaims_ConnectionStringHasUserSpecificAppName()
        {
            // Arrange & Act
            Mock<IHttpContextAccessor> httpContextAccessor = CreateHttpContextAccessorWithClaims(
                issuer: "https://login.microsoftonline.com/tenant-id/v2.0",
                objectId: "user-object-id-12345");

            (MsSqlQueryExecutor queryExecutor, RuntimeConfigProvider provider) = CreateQueryExecutorForPoolingTest(
                connectionString: "Server=localhost;Database=test;Application Name=TestApp;",
                enableObo: true,
                httpContextAccessor: httpContextAccessor);

            SqlConnection conn = queryExecutor.CreateConnection(provider.GetConfig().DefaultDataSourceName);
            SqlConnectionStringBuilder connBuilder = new(conn.ConnectionString);

            // Assert - Application Name should contain the base name plus |obo: and a hash
            // Note: The actual format includes version suffix, e.g., "TestApp,dab_oss_2.0.0|obo:{hash}"
            Assert.IsTrue(connBuilder.ApplicationName.Contains("|obo:"),
                $"Application Name should contain '|obo:' but was '{connBuilder.ApplicationName}'");
            Assert.IsTrue(connBuilder.ApplicationName.StartsWith("TestApp"),
                $"Application Name should start with 'TestApp' but was '{connBuilder.ApplicationName}'");
            Assert.IsTrue(connBuilder.Pooling, "Pooling should be enabled");
        }

        /// <summary>
        /// Test that different users get different pool hashes (different Application Names).
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public void TestObo_DifferentUsersGetDifferentPoolHashes()
        {
            // Arrange & Act - User 1
            Mock<IHttpContextAccessor> httpContextAccessor1 = CreateHttpContextAccessorWithClaims(
                issuer: "https://login.microsoftonline.com/tenant-id/v2.0",
                objectId: "user1-oid-aaaa");

            (MsSqlQueryExecutor queryExecutor1, RuntimeConfigProvider provider) = CreateQueryExecutorForPoolingTest(
                connectionString: "Server=localhost;Database=test;Application Name=DAB;",
                enableObo: true,
                httpContextAccessor: httpContextAccessor1);

            SqlConnection conn1 = queryExecutor1.CreateConnection(provider.GetConfig().DefaultDataSourceName);
            SqlConnectionStringBuilder connBuilder1 = new(conn1.ConnectionString);

            // Arrange & Act - User 2
            Mock<IHttpContextAccessor> httpContextAccessor2 = CreateHttpContextAccessorWithClaims(
                issuer: "https://login.microsoftonline.com/tenant-id/v2.0",
                objectId: "user2-oid-bbbb");

            (MsSqlQueryExecutor queryExecutor2, RuntimeConfigProvider provider2) = CreateQueryExecutorForPoolingTest(
                connectionString: "Server=localhost;Database=test;Application Name=DAB;",
                enableObo: true,
                httpContextAccessor: httpContextAccessor2);

            SqlConnection conn2 = queryExecutor2.CreateConnection(provider2.GetConfig().DefaultDataSourceName);
            SqlConnectionStringBuilder connBuilder2 = new(conn2.ConnectionString);

            // Assert - both should have |obo: in Application Name but different hashes
            // Note: The actual format includes version suffix, e.g., "DAB,dab_oss_2.0.0|obo:{hash}"
            Assert.IsTrue(connBuilder1.ApplicationName.Contains("|obo:"), "User 1 should have OBO pool prefix");
            Assert.IsTrue(connBuilder2.ApplicationName.Contains("|obo:"), "User 2 should have OBO pool prefix");
            Assert.AreNotEqual(connBuilder1.ApplicationName, connBuilder2.ApplicationName,
                "Different users should have different Application Names (different pool hashes)");
        }

        /// <summary>
        /// Test that when no user context is present (e.g., startup), connection string uses base Application Name.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public void TestOboNoUserContext_UsesBaseConnectionString()
        {
            // Arrange & Act
            Mock<IHttpContextAccessor> httpContextAccessor = CreateHttpContextAccessorWithClaims(issuer: string.Empty, objectId: string.Empty);

            (MsSqlQueryExecutor queryExecutor, RuntimeConfigProvider provider) = CreateQueryExecutorForPoolingTest(
                connectionString: "Server=localhost;Database=test;Application Name=BaseApp;",
                enableObo: true,
                httpContextAccessor: httpContextAccessor);

            SqlConnection conn = queryExecutor.CreateConnection(provider.GetConfig().DefaultDataSourceName);
            SqlConnectionStringBuilder connBuilder = new(conn.ConnectionString);

            // Assert - without user context, should use base Application Name (no |obo: suffix)
            // Note: The actual format includes version suffix, e.g., "BaseApp,dab_oss_2.0.0"
            Assert.IsTrue(connBuilder.ApplicationName.StartsWith("BaseApp"),
                $"Without user context, Application Name should start with 'BaseApp' but was '{connBuilder.ApplicationName}'");
            Assert.IsFalse(connBuilder.ApplicationName.Contains("|obo:"),
                $"Without user context, Application Name should not contain '|obo:' but was '{connBuilder.ApplicationName}'");
        }

        /// <summary>
        /// Test that when OBO is enabled and a user is authenticated but missing required claims
        /// (iss or oid/sub), CreateConnection throws DataApiBuilderException with OboAuthenticationFailure.
        /// This fail-safe behavior prevents cross-user connection pool contamination.
        /// </summary>
        [DataTestMethod, TestCategory(TestCategory.MSSQL)]
        [DataRow("https://login.microsoftonline.com/tenant/v2.0", null, "oid/sub",
            DisplayName = "Authenticated user with iss but missing oid/sub throws OboAuthenticationFailure")]
        [DataRow(null, "user-object-id", "iss",
            DisplayName = "Authenticated user with oid but missing iss throws OboAuthenticationFailure")]
        [DataRow(null, null, "iss and oid/sub",
            DisplayName = "Authenticated user with no claims throws OboAuthenticationFailure")]
        public void TestOboEnabled_AuthenticatedUserMissingClaims_ThrowsException(
            string? issuer,
            string? objectId,
            string missingClaimDescription)
        {
            // Arrange - Create an authenticated HttpContext with incomplete claims
            Mock<IHttpContextAccessor> httpContextAccessor = CreateHttpContextAccessorWithAuthenticatedUserMissingClaims(
                issuer: issuer,
                objectId: objectId);

            (MsSqlQueryExecutor queryExecutor, RuntimeConfigProvider provider) = CreateQueryExecutorForPoolingTest(
                connectionString: "Server=localhost;Database=test;Application Name=TestApp;",
                enableObo: true,
                httpContextAccessor: httpContextAccessor);

            // Act & Assert - CreateConnection should throw DataApiBuilderException
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
            {
                queryExecutor.CreateConnection(provider.GetConfig().DefaultDataSourceName);
            });

            Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode,
                $"Expected Unauthorized status code when missing {missingClaimDescription}");
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure, exception.SubStatusCode,
                $"Expected OboAuthenticationFailure sub-status code when missing {missingClaimDescription}");
            Assert.IsTrue(exception.Message.Contains("iss") && exception.Message.Contains("oid"),
                $"Exception message should mention required claims. Actual: {exception.Message}");
        }

        /// <summary>
        /// Creates an HttpContextAccessor mock with an authenticated user that has incomplete claims.
        /// Used to test fail-safe behavior when OBO is enabled but required claims are missing.
        /// </summary>
        /// <param name="issuer">The issuer claim value, or null to omit.</param>
        /// <param name="objectId">The oid claim value, or null to omit.</param>
        /// <returns>A configured HttpContextAccessor mock with authenticated user.</returns>
        private static Mock<IHttpContextAccessor> CreateHttpContextAccessorWithAuthenticatedUserMissingClaims(
            string? issuer,
            string? objectId)
        {
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DefaultHttpContext context = new();

            // Create an authenticated identity (passing authenticationType makes IsAuthenticated = true)
            System.Security.Claims.ClaimsIdentity identity = new("TestAuth");

            // Only add claims if they are provided (non-null)
            if (!string.IsNullOrEmpty(issuer))
            {
                identity.AddClaim(new System.Security.Claims.Claim("iss", issuer));
            }

            if (!string.IsNullOrEmpty(objectId))
            {
                identity.AddClaim(new System.Security.Claims.Claim("oid", objectId));
            }

            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            return httpContextAccessor;
        }

        #endregion

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }
    }
}

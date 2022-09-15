using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class RetryLogicUnitTests
    {
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

            Assert.AreEqual(expected, dbExceptionParser.IsTransientException(SqlExceptionCreator.Create(number)));
        }

        [TestMethod]
        public async Task TestRetryPolicyAsync()
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestCategory.MSSQL);
            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            DbExceptionParser dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);
            Mock<MsSqlQueryExecutor> queryExecutor = new(runtimeConfigProvider, dbExceptionParser, queryExecutorLogger.Object);

            // Mock the ExecuteQueryAgainstDbAsync to throw a transient exception.
            queryExecutor.Setup(x => x.ExecuteQueryAgainstDbAsync(
                It.IsAny<SqlConnection>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object>>()))
            .Throws(SqlExceptionCreator.Create(121));

            // Call the actual ExecuteQueryAsync method.
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, object>>())).CallBase();

            await Assert.ThrowsExceptionAsync<DataApiBuilderException>(async () =>
            {
                await queryExecutor.Object.ExecuteQueryAsync(sqltext: string.Empty, parameters: new Dictionary<string, object>());
            });
        }
    }

    public static class SqlExceptionCreator
    {
        public static SqlException Create(int number)
        {
            Exception? innerEx = null;
            ConstructorInfo[] c = typeof(SqlErrorCollection).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
            SqlErrorCollection errors = (c[0].Invoke(null) as SqlErrorCollection)!;
            List<object> errorList = (errors.GetType().GetField("_errors", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(errors) as List<object>)!;
            c = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
            ConstructorInfo nineC = c.FirstOrDefault(f => f.GetParameters().Length == 9)!;
            SqlError sqlError = (nineC.Invoke(new object?[] { number, (byte)0, (byte)0, "", "", "", (int)0, (uint)0, innerEx }) as SqlError)!;
            errorList.Add(sqlError);
            SqlException ex = (Activator.CreateInstance(typeof(SqlException), BindingFlags.NonPublic | BindingFlags.Instance, null, new object?[] { "test", errors,
            innerEx, Guid.NewGuid() }, null) as SqlException)!;
            return ex;
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

[TestClass, TestCategory("OracleUnit")]
public class OracleConnectionTests
{
    [DataTestMethod]
    [DataRow(60, true)]
    [DataRow(8177, true)]
    [DataRow(1033, true)]
    [DataRow(1034, true)]
    [DataRow(1089, true)]
    [DataRow(1090, true)]
    [DataRow(3113, true)]
    [DataRow(3114, true)]
    [DataRow(3135, true)]
    [DataRow(12170, true)]
    [DataRow(12535, true)]
    [DataRow(12514, true)]
    [DataRow(12516, true)]
    [DataRow(12518, true)]
    [DataRow(12519, true)]
    [DataRow(12520, true)]
    [DataRow(12528, true)]
    [DataRow(12537, true)]
    [DataRow(12541, true)]
    [DataRow(12543, true)]
    [DataRow(1017, false)] // Invalid credentials.
    [DataRow(28000, false)] // Locked account.
    [DataRow(12154, false)] // Unresolved connection identifier.
    [DataRow(12533, false)] // Invalid address parameters.
    [DataRow(1, false)] // Constraint violation.
    [DataRow(942, false)] // Missing table.
    [DataRow(20001, false)] // Database policy rejection.
    public void OracleConnectionErrorsAreClassifiedForRetry(int number, bool expected)
    {
        OracleTestFixture fixture = new();
        OracleDbExceptionParser parser = new(fixture.Config);
        Assert.AreEqual(expected, parser.IsTransientException(CreateException(number)));
    }

    [TestMethod]
    public void NonOracleErrorsAreNotRetried()
    {
        OracleTestFixture fixture = new();
        OracleDbExceptionParser parser = new(fixture.Config);
        Assert.IsFalse(parser.IsTransientException(new Mock<DbException>().Object));
    }

    [DataTestMethod]
    [DataRow(false, 12541, 1, 2)]
    [DataRow(true, 12541, 1, 2)]
    [DataRow(false, 12541, 3, 3)]
    [DataRow(true, 12541, 3, 3)]
    [DataRow(false, 1017, 1, 1)]
    [DataRow(true, 1017, 1, 1)]
    public async Task ConnectionRetryPolicyRecoversOrStopsAtExpectedAttempt(bool asynchronous, int number, int failures, int expectedAttempts)
    {
        OracleTestFixture fixture = new();
        OracleException error = CreateException(number);
        Mock<OracleQueryExecutor> executor = new(fixture.Config, new OracleDbExceptionParser(fixture.Config),
            NullLogger<IQueryExecutor>.Instance, fixture.Http, null)
        { CallBase = true };
        int attempts = 0;
        string ExecuteAttempt()
        {
            if (++attempts <= failures)
            {
                throw error;
            }

            return "connected";
        }

        executor.Setup(e => e.ExecuteQueryAgainstDbAsync(
            It.IsAny<OracleConnection>(), It.IsAny<string>(), It.IsAny<IDictionary<string, DbConnectionParam>>(),
            It.IsAny<Func<DbDataReader, List<string>, Task<string>>>(), It.IsAny<HttpContext>(),
            fixture.SourceName, It.IsAny<List<string>>()))
            .Returns(() => Task.FromResult(ExecuteAttempt()));
        executor.Setup(e => e.ExecuteQueryAgainstDb(
            It.IsAny<OracleConnection>(), It.IsAny<string>(), It.IsAny<IDictionary<string, DbConnectionParam>>(),
            It.IsAny<Func<DbDataReader, List<string>, string>>(), It.IsAny<HttpContext>(),
            fixture.SourceName, It.IsAny<List<string>>()))
            .Returns(ExecuteAttempt);

        async Task<string> Execute() => asynchronous
            ? await executor.Object.ExecuteQueryAsync<string>("SELECT 1 FROM DUAL", new Dictionary<string, DbConnectionParam>(), null, fixture.SourceName)
            : executor.Object.ExecuteQuery<string>("SELECT 1 FROM DUAL", new Dictionary<string, DbConnectionParam>(), null);

        if (expectedAttempts > failures)
        {
            Assert.AreEqual("connected", await Execute());
        }
        else
        {
            DataApiBuilderException failure = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(Execute);
            Assert.AreSame(error, failure.InnerException);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed, failure.SubStatusCode);
        }

        Assert.AreEqual(expectedAttempts, attempts);
    }

    private static OracleException CreateException(int number)
    {
        // ODP.NET has no public exception constructor. Match its internal signature explicitly,
        // as SqlTestHelper does for SqlException, and exercise the real provider exception type.
        ConstructorInfo constructor = typeof(OracleException).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, new[] { typeof(int), typeof(string), typeof(string), typeof(string), typeof(int) }, modifiers: null);
        Assert.IsNotNull(constructor);
        OracleException exception = (OracleException)constructor.Invoke(new object[] { number, "Test connection failure", "", "", 0 });
        Assert.AreEqual(number, exception.Number);
        return exception;
    }
}

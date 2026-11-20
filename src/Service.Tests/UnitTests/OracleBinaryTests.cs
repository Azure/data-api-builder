// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

[TestClass, TestCategory("OracleUnit")]
public class OracleBinaryTests
{
    [DataTestMethod]
    [DataRow(16, false, OracleDbType.Raw)]
    [DataRow(16, true, OracleDbType.Raw)]
    [DataRow(-1, false, OracleDbType.Blob)]
    [DataRow(-1, true, OracleDbType.Blob)]
    public void BinaryParametersUseNativeRawOrBlobBinds(int length, bool isNull, OracleDbType expected)
    {
        OracleTestFixture fixture = new();
        byte[] bytes = new byte[] { 0, 1, 254, 255 };
        Dictionary<string, DbConnectionParam> parameters = new()
        {
            ["@binary"] = new(isNull ? null : bytes, DbType.Binary, length: length)
        };
        using OracleConnection connection = new();
        using OracleCommand command = (OracleCommand)fixture.Executor.PrepareDbCommand(connection,
            "SELECT :binary FROM DUAL", parameters, null, fixture.SourceName);
        Assert.AreEqual(expected, command.Parameters[0].OracleDbType);
        Assert.AreEqual(isNull ? System.DBNull.Value : bytes, command.Parameters[0].Value);
    }

    [DataTestMethod]
    [DataRow(16, OracleDbType.Raw)]
    [DataRow(-1, OracleDbType.Blob)]
    public void Base64ParameterStringsAreDecodedBeforeBinding(int length, OracleDbType expected)
    {
        OracleTestFixture fixture = new();
        using OracleConnection connection = new();
        using OracleCommand command = (OracleCommand)fixture.Executor.PrepareDbCommand(connection, "SELECT :binary FROM DUAL",
            new Dictionary<string, DbConnectionParam> { ["binary"] = new("AAH+/w==", DbType.Binary, length: length) }, null, fixture.SourceName);
        Assert.AreEqual(expected, command.Parameters[0].OracleDbType);
        Assert.IsInstanceOfType(command.Parameters[0].Value, typeof(byte[]));
        CollectionAssert.AreEqual(new byte[] { 0, 1, 254, 255 }, (byte[])command.Parameters[0].Value);
    }
}

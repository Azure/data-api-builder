// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.Data.Common;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

[TestClass, TestCategory("OracleUnit")]
public class OracleConnectionLeaseTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DisposingConnectionLeaseTwiceReleasesItsResourceExactlyOnce(bool providerManaged)
    {
        CountingConnection connection = new();
        int releasedLeases = 0;
        QueryExecutor<CountingConnection>.ConnectionLease lease = new(connection,
            providerManaged ? () => releasedLeases++ : null);

        lease.Dispose();
        lease.Dispose();

        Assert.AreEqual(providerManaged ? 1 : 0, releasedLeases);
        Assert.AreEqual(providerManaged ? 0 : 1, connection.DisposeCalls);
    }

    private sealed class CountingConnection : DbConnection
    {
        public CountingConnection() { }

        public int DisposeCalls { get; private set; }
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() => throw new NotSupportedException();
        public override void Close() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCalls++;
            }

            base.Dispose(disposing);
        }
    }
}

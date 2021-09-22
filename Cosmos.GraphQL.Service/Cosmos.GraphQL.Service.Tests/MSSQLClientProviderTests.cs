﻿using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Data;
using System.Data.Common;

namespace Cosmos.GraphQL.Service.Tests
{
    [TestClass]
    public class MSSQLClientProviderTests
    {
        private IDbConnectionService _clientProvider;
        public MSSQLClientProviderTests()
        {
            _clientProvider = new MsSqlClientProvider();
        }

        /// <summary>
        /// Ensure a connection is successfully opened within the [Database]ClientProvider,
        /// given a valid connection string.
        /// </summary>
        [TestMethod]
        [Ignore] // TODO: moderakh we will re-enable, once we can run all components tests in the CI
        public void TestOpenConnection()
        {
            DbConnection connection = _clientProvider.GetClient();
            Console.WriteLine("ServerVersion: {0}", connection.ServerVersion);
            Console.WriteLine("State: {0}", connection.State);
            Assert.IsTrue(connection.State.Equals(ConnectionState.Open));
            connection.Dispose();
        }
    }
}

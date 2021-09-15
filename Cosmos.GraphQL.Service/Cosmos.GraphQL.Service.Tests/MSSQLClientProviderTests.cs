using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests
{
    [TestClass]
    public class MSSQLClientProviderTests
    {
        private IClientProvider<SqlConnection> _clientProvider;
        public MSSQLClientProviderTests()
        {
            _clientProvider = new MSSQLClientProvider();
        }
        /// <summary>
        /// Ensure a connection is successfully opened within the [Database]ClientProvider,
        /// given a valid connection string.
        /// </summary>
        [TestMethod]
        public void TestOpenConnection()
        {
            SqlConnection connection = _clientProvider.getClient();
            connection.Open();
            Console.WriteLine("ServerVersion: {0}", connection.ServerVersion);
            Console.WriteLine("State: {0}", connection.State);
            Assert.IsTrue(connection.State.Equals(ConnectionState.Open));
            connection.Dispose();
        }
    }
}

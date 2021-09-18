using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.configurations
{
    /// <summary>
    /// Representation of MsSql specific credential properties
    /// </summary>
    public class MsSqlCredentials : IDatabaseCredentials
    {
        /// <summary>
        /// Fully constructed connection string with user credentials. 
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Server name to connect to.
        /// </summary>
        public string Server { get; set; }
    }
}

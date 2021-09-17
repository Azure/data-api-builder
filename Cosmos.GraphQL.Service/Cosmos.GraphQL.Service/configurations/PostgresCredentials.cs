using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.configurations
{
    /// <summary>
    /// Represenation of POSTGRES specific credential properties
    /// </summary>
    public class PostgresCredentials : IDatabaseCredentials
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

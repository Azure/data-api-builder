using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.configurations
{
    public class SQLCredentials : IDatabaseCredentials
    {
        public string ConnectionString { get; set; }
        public string Server { get; set; }
    }
}

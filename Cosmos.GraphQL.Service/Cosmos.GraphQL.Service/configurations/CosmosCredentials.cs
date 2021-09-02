using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.configurations
{
    public class CosmosCredentials : IDatabaseCredentials
    {
        public string ConnectionString { get; set; }
        public string EndpointUrl { get; set; }
        public string AuthorizationKey { get; set; }
        public string Container { get; set; }
    }
}

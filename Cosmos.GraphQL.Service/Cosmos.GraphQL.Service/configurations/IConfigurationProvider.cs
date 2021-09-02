using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.configurations
{
    /// <summary>
    /// Provider for the singleton instance of a connection provider to a specific database 
    /// </summary>
    public interface IConfigurationProvider
    {        
        public IConfigurationProvider getInstance();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Interface representing database clients with retrieval method.
    /// </summary>
    /// <typeparam name="T">Type of database client (i.e. SqlConnection or CosmosClient)</typeparam>
    public interface IClientProvider<T>
    {
        public T getClient();
    }
}

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Interface representing database clients with retrieval method.
    /// </summary>
    /// <typeparam name="T">Type of database client (i.e. DbConnection or CosmosClient)</typeparam>
    public interface IClientProvider<T>
    {
        public T GetClient();
    }
}

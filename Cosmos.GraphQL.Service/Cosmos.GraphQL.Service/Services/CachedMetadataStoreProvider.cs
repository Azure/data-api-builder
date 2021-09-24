using System;
using System.Collections.Concurrent;
using Cosmos.GraphQL.Service.Models;

namespace Cosmos.GraphQL.Services
{
    public class MetadataStoreException : Exception
    {
        public MetadataStoreException(string message)
            : base(message)
        {
        }

        public MetadataStoreException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class CachedMetadataStoreProvider : IMetadataStoreProvider
    {
        private readonly IMetadataStoreProvider _storeProvider;
        private string _schema;
        private ConcurrentDictionary<string, MutationResolver> _mutationResolvers = new ConcurrentDictionary<string, MutationResolver>();
        private ConcurrentDictionary<string, GraphQLQueryResolver> _queryResolvers = new ConcurrentDictionary<string, GraphQLQueryResolver>();

        public CachedMetadataStoreProvider(DocumentMetadataStoreProvider storeProvider)
        {
            this._storeProvider = storeProvider;
        }

        public void StoreGraphQLSchema(string schema)
        {
            this._schema = schema;
            this._storeProvider.StoreGraphQLSchema(schema);
        }

        public string GetGraphQLSchema()
        {
            if (this._schema == null)
            {
                // TODO: concurrent invocation
                this._schema = _storeProvider.GetGraphQLSchema();
            }

            return this._schema;
        }

        public MutationResolver GetMutationResolver(string name)
        {
            // TODO: optimize for if multiple threads ask for the same name same time? 
            return _mutationResolvers.GetOrAdd(name, (name) => this._storeProvider.GetMutationResolver(name));

        }

        public GraphQLQueryResolver GetQueryResolver(string name)
        {
            // TODO: optimize for if multiple threads ask for the same name same time? 
            return _queryResolvers.GetOrAdd(name, (name) => this._storeProvider.GetQueryResolver(name));
        }

        public void StoreMutationResolver(MutationResolver mutationResolver)
        {
            // TODO: guard for if multiple threads update.
            _mutationResolvers.AddOrUpdate(mutationResolver.id, mutationResolver, (s, resolver) => resolver);
            _storeProvider.StoreMutationResolver(mutationResolver);
        }

        public void StoreQueryResolver(GraphQLQueryResolver queryResolver)
        {
            // TODO: guard for if multiple threads update.
            _queryResolvers.AddOrUpdate(queryResolver.id, queryResolver, (s, resolver) => resolver);
            _storeProvider.StoreQueryResolver(queryResolver);
        }
    }
}

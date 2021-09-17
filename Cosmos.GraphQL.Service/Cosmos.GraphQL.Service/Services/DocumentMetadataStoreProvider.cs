using System;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.Azure.Cosmos;

namespace Cosmos.GraphQL.Services
{
    public class DocumentMetadataStoreProvider : IMetadataStoreProvider
    {
        private readonly CosmosClient client;
        private readonly string systemDatabaseName = "_systemGraphQL";
        private readonly string systemContainerName = "_systemGraphQL";
        private readonly string schemaId = ":activeSchema";
        private readonly Container container;

        public DocumentMetadataStoreProvider(CosmosClientProvider client)
        {
            this.client = client.getCosmosClient();
            this.container = this.client.GetDatabase(systemDatabaseName).GetContainer(systemContainerName);
            CreateSystemContainerIfDoesNotExist();
        }

        private async void CreateSystemContainerIfDoesNotExist()
        {
            await this.client.CreateDatabaseIfNotExistsAsync(systemDatabaseName);
            await this.client.GetDatabase(systemDatabaseName).CreateContainerIfNotExistsAsync(systemContainerName, "/id");
        }
        
        public void StoreGraphQLSchema(string schema)
        {
            var item = new SchemaDocument()
            {
                schema = schema,
                id = schemaId
            };

            container.UpsertItemAsync(item);
        }

        public string GetGraphQLSchema()
        {
            SchemaDocument doc = container.ReadItemAsync<SchemaDocument>(schemaId, new PartitionKey(schemaId)).Result;
            return doc.schema;
        }

        public MutationResolver GetMutationResolver(string name)
        {
            return container.ReadItemAsync<MutationResolver>(name, new PartitionKey(name)).Result;
        }

        public GraphQLQueryResolver GetQueryResolver(string name)
        {
            return container.ReadItemAsync<GraphQLQueryResolver>(name, new PartitionKey(name)).Result;
        }

        public void StoreMutationResolver(MutationResolver mutationResolver)
        {
            container.UpsertItemAsync(mutationResolver);
        }

        public void StoreQueryResolver(GraphQLQueryResolver queryResolver)
        {
            container.UpsertItemAsync(queryResolver);
        }
    }
}
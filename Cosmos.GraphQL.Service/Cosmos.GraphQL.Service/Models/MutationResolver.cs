using System.Collections.Generic;
using Microsoft.Azure.Cosmos;

namespace Cosmos.GraphQL.Service.Models
{
    public class MutationResolver
    {
        public string graphQLMutationName { get; set; }
        
        // TODO: add enum support
        public string Operation { get; set; }

        public CosmosIdentifier Identifier;
        
        // TODO: add support for partitionKey
        //        public PartitionKey PartitionKey;

        public string dotNetCodeRequestHandler { get; set; }
        public string dotNetCodeResponseHandler { get; set; }
    }

    public enum Operation
    {
        Upsert, Delete, Create
    }
}
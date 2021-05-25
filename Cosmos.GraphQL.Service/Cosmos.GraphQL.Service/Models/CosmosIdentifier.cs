using Microsoft.Azure.Cosmos;

namespace Cosmos.GraphQL.Service.Models
{
    public class CosmosIdentifier
    {
        public PartitionKey PartitionKey;
        public string id;
    }
}
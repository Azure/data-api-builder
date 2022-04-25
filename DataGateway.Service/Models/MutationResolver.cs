using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Models
{
    public record MutationResolver(string Id, Operation OperationType, string DatabaseName, string ContainerName, string Fields, string Table);
}

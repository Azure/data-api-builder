using Azure.DataApiBuilder.Config;

namespace Azure.DataApiBuilder.Service.Models
{
    public record MutationResolver(string Id, Operation OperationType, string DatabaseName, string ContainerName, string Fields, string Table);
}

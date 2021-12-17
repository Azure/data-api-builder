namespace Azure.DataGateway.Service.Models
{
    public class MutationResolver
    {
        public string Id { get; set; }

        // TODO: add enum support
        public string OperationType { get; set; }
        public string DatabaseName { get; set; }
        public string ContainerName { get; set; }
        public string Fields { get; set; }
        public string Table { get; set; }
    }

    public enum Operation
    {
        Upsert, Delete, Create
    }
}

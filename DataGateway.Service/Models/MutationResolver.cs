namespace Azure.DataGateway.Service.Models
{
    public class MutationResolver
    {
        public string Id { get; set; }
        public MutationOperation OperationType { get; set; }
        public string DatabaseName { get; set; }
        public string ContainerName { get; set; }
        public string Fields { get; set; }
        public string Table { get; set; }
    }

    public enum MutationOperation
    {
        None,
        // Cosmos Operations
        Upsert, Delete, Create,

        // Sql Operations
        Insert, Update
    }
}

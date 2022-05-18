namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// Metadata required to resolve a specific GraphQL type.
    /// </summary>
    /// <param name="IsPaginationType">Shows if the type is a *Connection pagination result type</param>
    /// <param name="DatabaseName">The name of the database that this GraphQL type corresponds to.</param>
    /// <param name="ContainerName">The name of the container that this GraphQL type corresponds to.</param>
    public record GraphQLType(bool IsPaginationType, string DatabaseName, string ContainerName);
}

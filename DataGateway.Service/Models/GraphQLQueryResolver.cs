using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models
{
    public record GraphQLQueryResolver(string Id, string DatabaseName, string ContainerName, string ParametrizedQuery, QuerySpec QuerySpec, bool IsList, bool IsPaginated);

    public record QuerySpec(string QueryString, List<string> QueryParams);
}

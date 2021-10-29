using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models
{
    public class GraphQLQueryResolver
    {
        public string Id { get; set; }
        public string DatabaseName { get; set; }
        public string ContainerName { get; set; }
        public string ParametrizedQuery { get; set; }
        public QuerySpec QuerySpec { get; set; }
        public bool IsList { get; set; }
    }

    public class QuerySpec
    {
        public string QueryString { get; set; }
        public List<string> QueryParams { get; set; }
    }
}

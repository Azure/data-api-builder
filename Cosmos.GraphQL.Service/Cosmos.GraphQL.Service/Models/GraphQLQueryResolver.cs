using System;
using System.Collections.Generic;

namespace Cosmos.GraphQL.Service.Models
{
    public class GraphQLQueryResolver
    {
        public string id { get; set; }
        public string databaseName { get; set; }
        public string containerName { get; set; }
        public string parametrizedQuery { get; set; }
        public QuerySpec QuerySpec { get; set; }
        public bool isList { get; set; }
    }

    public class QuerySpec
    {
        public string QueryString { get; set; }
        public List<string> queryParams { get; set; }
    }
}
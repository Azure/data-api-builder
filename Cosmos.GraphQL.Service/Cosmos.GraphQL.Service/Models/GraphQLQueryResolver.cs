using System;
using System.Collections.Generic;

namespace Cosmos.GraphQL.Service.Models
{
    public class GraphQLQueryResolver
    {
        public string GraphQLQueryName { get; set; }
        public QuerySpec QuerySpec { get; set; }
        
        public string dotNetCodeRequestHandler { get; set; }
        public string dotNetCodeResponseHandler { get; set; }
    }
    

    public class QuerySpec
    {
        public string QueryString { get; set; }
        public List<string> queryParams { get; set; }
    }
}
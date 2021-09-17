using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Models
{
    public class GraphQLRequest
    {
        internal string Query { get; set; }
        internal string OperationName { get; set; }
        //internal Inputs Variables { get; set; }
    }
}

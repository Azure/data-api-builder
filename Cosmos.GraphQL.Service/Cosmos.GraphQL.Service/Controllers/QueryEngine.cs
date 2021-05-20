using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace Cosmos.GraphQL.Service.Controllers
{
    public class QueryEngine
    {
        private Dictionary<string, GraphQLQueryResolver> resolvers = new Dictionary<string, GraphQLQueryResolver>();

        public void registerResolver(GraphQLQueryResolver resolver)
        {
            resolvers.Add(resolver.GraphQLQueryName, resolver);
        }

        public async Task<string> execute(string graphQLQueryName, Dictionary<string, string> parameters)
        {

            GraphQLQueryResolver resolver = null;
            // TODO: 
            resolvers.TryGetValue(graphQLQueryName, out resolver);
            if (resolver == null)
            {
                // TODO: throw error
            }
            
            // assert resolver != null
            int result = await CSharpScript.EvaluateAsync<int>("1 + 2");
            return result.ToString();
        }
    }
}
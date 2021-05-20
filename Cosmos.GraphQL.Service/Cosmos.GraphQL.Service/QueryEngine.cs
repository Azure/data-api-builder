using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace Cosmos.GraphQL.Service
{
    public class QueryEngine
    {
        private readonly Dictionary<string, GraphQLQueryResolver> resolvers = new Dictionary<string, GraphQLQueryResolver>();

        public void registerResolver(GraphQLQueryResolver resolver)
        {
            resolvers.Add(resolver.GraphQLQueryName, resolver);
        }

        public async Task<string> execute(string graphQLQueryName, Dictionary<string, string> parameters)
        {
            if (!resolvers.TryGetValue(graphQLQueryName, out var resolver))
            {
                throw new NotImplementedException($"{graphQLQueryName} doesn't exist");
            }
            
            // assert resolver != null
            int result = await CSharpScript.EvaluateAsync<int>(resolver.dotNetCodeRequestHandler);
            return result.ToString();
        }
    }
}
namespace Cosmos.GraphQL.Service.Resolvers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Threading.Tasks;

    public sealed class Globals
    {
        public static class Cosmos
        {
            public static string Key { get; internal set; }

            public static string Endpoint { get; internal set; }
        }
        
        internal static void Initialize()
        {
            Cosmos.Key = Environment.GetEnvironmentVariable("COSMOS_KEY");
            Cosmos.Endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT");
        }
    }
}
using System.Configuration;
using Cosmos.GraphQL.Service.configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using ConfigurationProvider = Cosmos.GraphQL.Service.configurations.ConfigurationProvider;

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
            public static Container Container { get; internal set; }
        }
        
        internal static void Initialize(Container CosmosContainer)
        {
            Cosmos.Container = CosmosContainer;

        }
    }
}
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using HotChocolate.Resolvers;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    public class CosmosExistsQueryStructure : CosmosQueryStructure
    {
        /// <summary>
        /// Constructor for Exists query.
        /// </summary>
        public CosmosExistsQueryStructure(IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            RuntimeConfigProvider runtimeConfigProvider,
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IncrementingInteger? counter = null,
            List<Predicate>? predicates = null)
            : base(context,
                  parameters,
                  runtimeConfigProvider,
                  metadataProvider,
                  authorizationResolver,
                  gQLFilterParser,
                  counter,
                  predicates)
        {
            SourceAlias = CreateTableAlias();
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using HotChocolate.Resolvers;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    public class CosmosExistsQueryStructure : CosmosQueryStructure
    {
        public CosmosExistsQueryStructure(IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IncrementingInteger? counter = null,
            List<Predicate>? predicates = null)
            : base(context,
                  parameters,
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
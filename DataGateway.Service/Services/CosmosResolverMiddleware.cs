using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Services
{
    public class CosmosResolverMiddlewareMaker : IResolverMiddlewareMaker
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly IMetadataStoreProvider _metadataStoreProvider;

        public CosmosResolverMiddlewareMaker(IQueryEngine queryEngine, IMutationEngine mutationEngine, IMetadataStoreProvider metadataStoreProvider)
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _metadataStoreProvider = metadataStoreProvider;
        }

        public ResolverMiddleware MakeWith(FieldDelegate next)
        {
            return new CosmosResolverMiddleware(next, _queryEngine, _mutationEngine, _metadataStoreProvider);
        }
    }

    /// <summary>
    /// HotChocolate Resolver Middleware when environment is Cosmos
    /// </summary>
    public class CosmosResolverMiddleware : ResolverMiddleware
    {
        public CosmosResolverMiddleware(FieldDelegate next, IQueryEngine queryEngine, IMutationEngine mutationEngine, IMetadataStoreProvider metadataStoreProvider)
            : base(next, queryEngine, mutationEngine, metadataStoreProvider) { }

        public override async Task InvokeAsync(IMiddlewareContext context)
        {
            JsonElement jsonElement;
            if (context.Selection.Field.Coordinate.TypeName.Value == "Mutation")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);

                context.Result = await _mutationEngine.ExecuteAsync(context, parameters);
            }
            else if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);
                bool isPaginatedQuery = IsPaginatedQuery(context.Selection.Field.Name.Value);

                if (context.Selection.Type.IsListType())
                {
                    context.Result = await _queryEngine.ExecuteListAsync(context, parameters);
                }
                else
                {
                    context.Result = await _queryEngine.ExecuteAsync(context, parameters, isPaginatedQuery);
                }
            }
            else if (context.Selection.Field.Type.IsLeafType())
            {
                // This means this field is a scalar, so we don't need to do
                // anything for it.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    context.Result = jsonElement.ToString();
                }
            }
            else if (IsInnerObject(context))
            {
                // This means it's a field that has another custom type as its
                // type, so there is a full JSON object inside this key. For
                // example such a JSON object could have been created by a
                // One-To-Many join.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    //TODO: Try to avoid additional deserialization/serialization here.
                    context.Result = JsonDocument.Parse(jsonElement.ToString());
                }
            }
            else if (context.Selection.Type.IsListType())
            {
                // This means the field is a list and HotChocolate requires
                // that to be returned as a List of JsonDocuments. For example
                // such a JSON list could have been created by a One-To-Many
                // join.
                if (TryGetPropertyFromParent(context, out jsonElement))
                {
                    //TODO: Try to avoid additional deserialization/serialization here.
                    context.Result = JsonSerializer.Deserialize<List<JsonDocument>>(jsonElement.ToString());
                }
            }

            await _next(context);
        }

        /// <summary>
        /// Identifies if a query is paginated or not by checking the IsPaginated param on the respective resolver.
        /// </summary>
        /// <param name="queryName the name of the query"></param>
        /// <returns></returns>
        private bool IsPaginatedQuery(string queryName)
        {
            GraphQLQueryResolver resolver = _metadataStoreProvider.GetQueryResolver(queryName);
            if (resolver == null)
            {
                string message = string.Format("There is no resolver for the query: {0}", queryName);
                throw new InvalidOperationException(message);
            }

            return resolver.IsPaginated;
        }
    }
}


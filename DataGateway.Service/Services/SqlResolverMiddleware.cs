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
    public class SqlResolverMiddlewareMaker : IResolverMiddlewareMaker
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly IMetadataStoreProvider _metadataStoreProvider;

        public SqlResolverMiddlewareMaker(IQueryEngine queryEngine, IMutationEngine mutationEngine, IMetadataStoreProvider metadataStoreProvider)
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _metadataStoreProvider = metadataStoreProvider;
        }

        public ResolverMiddleware MakeWith(FieldDelegate next)
        {
            return new SqlResolverMiddleware(next, _queryEngine, _mutationEngine, _metadataStoreProvider);
        }
    }

    /// <summary>
    /// HotChocolate Resolver Middleware when environment is Postgres or MsSql
    /// </summary>
    public class SqlResolverMiddleware : ResolverMiddleware
    {
        private readonly string _contextDataLabel = "metadata";

        public SqlResolverMiddleware(FieldDelegate next, IQueryEngine queryEngine, IMutationEngine mutationEngine, IMetadataStoreProvider metadataStoreProvider)
            : base(next, queryEngine, mutationEngine, metadataStoreProvider) { }

        public override async Task InvokeAsync(IMiddlewareContext context)
        {
            JsonElement jsonElement;
            // PaginationMetadata metadata;
            if (context.Selection.Field.Coordinate.TypeName.Value == "Mutation")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);

                Tuple<JsonDocument, PaginationMetadata> result = await _mutationEngine.ExecuteAsyncWithMetadata(context, parameters);
                context.Result = result.Item1;
                context.ScopedContextData = context.ScopedContextData.SetItem(_contextDataLabel, result.Item2);
            }
            else if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);

                if (context.Selection.Type.IsListType())
                {
                    Tuple<IEnumerable<JsonDocument>, PaginationMetadata> result = await _queryEngine.ExecuteListAsyncWithMetadata(context, parameters);
                    context.Result = result.Item1;
                    context.ScopedContextData = context.ScopedContextData.SetItem(_contextDataLabel, result.Item2);
                }
                else
                {
                    Tuple<JsonDocument, PaginationMetadata> result = await _queryEngine.ExecuteAsyncWithMetadata(context, parameters);

                    if (result.Item2.IsPaginated)
                    {
                        context.Result = SqlPaginationUtil.CreatePaginationConnectionFromJsonDocument(result.Item1, result.Item2);
                        ;
                    }
                    else
                    {
                        context.Result = result.Item1;
                    }

                    context.ScopedContextData = context.ScopedContextData.SetItem(_contextDataLabel, result.Item2);
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
                    PaginationMetadata parentMetadata = (PaginationMetadata)context.ScopedContextData[_contextDataLabel];
                    PaginationMetadata currentMetadata = parentMetadata.Subqueries[context.Selection.Field.Name.Value];

                    if (currentMetadata.IsPaginated)
                    {
                        context.Result = SqlPaginationUtil.CreatePaginationConnectionFromJsonElement(jsonElement, currentMetadata);
                    }
                    else
                    {
                        //TODO: Try to avoid additional deserialization/serialization here.
                        context.Result = JsonDocument.Parse(jsonElement.ToString());
                    }

                    context.ScopedContextData = context.ScopedContextData.SetItem(_contextDataLabel, currentMetadata);
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
                    PaginationMetadata parentMetadata = (PaginationMetadata)context.ScopedContextData[_contextDataLabel];

                    //TODO: Try to avoid additional deserialization/serialization here.
                    context.Result = JsonSerializer.Deserialize<List<JsonDocument>>(jsonElement.ToString());

                    if (!parentMetadata.IsPaginated)
                    {
                        // fetch the next metadata object if the parent is not paginated
                        // if the parent is paginated, *Conneciton.items will be filtered as list type, but items
                        // no not have a PaginationMetadata associated with them so the dictionary look up below will fail
                        PaginationMetadata currentMetadata = parentMetadata.Subqueries[context.Selection.Field.Name.Value];
                        context.ScopedContextData = context.ScopedContextData.SetItem(_contextDataLabel, currentMetadata);
                    }
                }
            }

            await _next(context);
        }
    }
}


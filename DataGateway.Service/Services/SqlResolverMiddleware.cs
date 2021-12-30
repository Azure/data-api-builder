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
        private static readonly string _contextMetadata = "metadata";
        private static readonly string _skippedMetadataUpdateForItems = "skippedMetadataUpdateForItems";

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
                SetNewMetadata(context, result.Item2);
            }
            else if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);

                if (context.Selection.Type.IsListType())
                {
                    Tuple<IEnumerable<JsonDocument>, PaginationMetadata> result = await _queryEngine.ExecuteListAsyncWithMetadata(context, parameters);
                    context.Result = result.Item1;
                    SetNewMetadata(context, result.Item2);
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

                    SetNewMetadata(context, result.Item2);
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
                    Console.WriteLine($"Inner {context.Selection.Field.Name.Value}");
                    PaginationMetadata parentMetadata = (PaginationMetadata)context.ScopedContextData[_contextMetadata];
                    PaginationMetadata currentMetadata = parentMetadata.Subqueries[context.Selection.Field.Name.Value];
                    Console.WriteLine($"Inner metadata keys: {string.Join(" ", parentMetadata.Subqueries.Keys)}");

                    if (currentMetadata.IsPaginated)
                    {
                        context.Result = SqlPaginationUtil.CreatePaginationConnectionFromJsonElement(jsonElement, currentMetadata);
                    }
                    else
                    {
                        //TODO: Try to avoid additional deserialization/serialization here.
                        context.Result = JsonDocument.Parse(jsonElement.ToString());
                    }

                    SetNewMetadata(context, currentMetadata);
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
                    Console.WriteLine($"List {context.Selection.Field.Name.Value}");
                    PaginationMetadata parentMetadata = (PaginationMetadata)context.ScopedContextData[_contextMetadata];
                    Console.WriteLine($"List metadata keys: {string.Join(" ", parentMetadata.Subqueries.Keys)}");

                    //TODO: Try to avoid additional deserialization/serialization here.
                    context.Result = JsonSerializer.Deserialize<List<JsonDocument>>(jsonElement.ToString());

                    bool skippedMetadataUpdateForItems = (bool)context.ScopedContextData[_skippedMetadataUpdateForItems];
                    if (!parentMetadata.IsPaginated || (parentMetadata.IsPaginated && skippedMetadataUpdateForItems))
                    {
                        // if the parent is paginated, *Conneciton.items will be filtered as list type, but items
                        // no not have a PaginationMetadata associated with them so the dictionary look up below will fail
                        // only override the pagination metadata if the parent query was not paginated OR
                        // the parent query is paginated and *Connection.items have been skipped
                        PaginationMetadata currentMetadata = parentMetadata.Subqueries[context.Selection.Field.Name.Value];
                        SetNewMetadata(context, currentMetadata);
                    }
                    else
                    {
                        // mark that *Connection.items have been skipped
                        context.ScopedContextData = context.ScopedContextData.SetItem(_skippedMetadataUpdateForItems, true);
                    }
                }
            }

            await _next(context);
        }

        /// <summary>
        /// Set new metadata and reset the depth that the metadata has persisted
        /// </summary>
        private static void SetNewMetadata(IMiddlewareContext context, PaginationMetadata metadata)
        {
            context.ScopedContextData = context.ScopedContextData.SetItem(_contextMetadata, metadata);
            context.ScopedContextData = context.ScopedContextData.SetItem(_skippedMetadataUpdateForItems, false);
        }
    }
}


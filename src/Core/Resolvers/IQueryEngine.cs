// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Core.Models;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Interface for execution of queries against a database.
    /// </summary>
    public interface IQueryEngine
    {
        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json back
        /// </summary>
        /// <returns>
        /// returns the json result and a metadata object required to resolve the Json.
        /// </returns>
        public Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> parameters, string dataSourceName);

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL and expecting a
        /// list of Jsons back.
        /// </summary>
        /// <returns>
        /// returns the list of jsons result and a metadata object required to resolve the Json.
        /// </returns>
        public Task<Tuple<IEnumerable<JsonDocument>, IMetadata?>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object?> parameters, string dataSourceName);

        /// <summary>
        /// Given the RestRequestContext, obtains the query text and executes it against the backend.
        /// </summary>
        public Task<JsonDocument?> ExecuteAsync(FindRequestContext context);

        /// <summary>
        /// Given the StoredProcedureRequestContext, obtains the query text and executes it against the backend.
        /// </summary>
        public Task<IActionResult> ExecuteAsync(StoredProcedureRequestContext context, string dataSourceName);

        /// <summary>
        /// Resolves a jsonElement representing an inner object based on the field's schema and metadata
        /// </summary>
        public JsonDocument? ResolveInnerObject(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata);

        /// <summary>
        /// Resolves a jsonElement representing a list type based on the field's schema and metadata
        /// </summary>
        public object? ResolveListType(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata);
    }
}

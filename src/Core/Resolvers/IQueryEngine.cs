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
        /// Executes the given IMiddlewareContext of the GraphQL query and expects a list of JsonDocument objects back.
        /// This method accepts a list of PKs for which to construct and return the response.
        /// </summary>
        /// <param name="context">IMiddleware context of the GraphQL query</param>
        /// <param name="parameters">List of PKs for which the response Json have to be computed and returned.
        /// Each Pk is represented by a dictionary where (key, value) as (column name, column value).
        /// Primary keys can be of composite and be of any type. Hence, the decision to represent
        /// a PK as Dictionary<string, object?>
        /// </param>
        /// <param name="dataSourceName">DataSource name</param>
        /// <returns>Returns the json result and metadata object for the given list of PKs</returns>
        public Task<Tuple<JsonDocument?, IMetadata?>> ExecuteMultipleCreateFollowUpQueryAsync(IMiddlewareContext context, List<IDictionary<string, object?>> parameters, string dataSourceName) => throw new NotImplementedException();

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
        public JsonElement ResolveObject(JsonElement element, ObjectField fieldSchema, ref IMetadata metadata);

        /// <summary>
        /// Resolves a jsonElement representing a list type based on the field's schema and metadata
        /// </summary>
        public object ResolveList(JsonElement array, ObjectField fieldSchema, ref IMetadata? metadata);
    }
}

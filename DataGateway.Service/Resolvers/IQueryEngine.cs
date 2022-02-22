using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Resolvers
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
        public Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters);

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL and expecting a
        /// list of Jsons back.
        /// </summary>
        /// <returns>
        /// returns the list of jsons result and a metadata object required to resolve the Json.
        /// </returns>
        public Task<Tuple<IEnumerable<JsonDocument>, IMetadata>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters);

        /// <summary>
        /// Given the RestRequestContext structure, obtains the query text and executes it against the backend.
        /// </summary>
        public Task<JsonDocument> ExecuteAsync(RestRequestContext queryStructure);

        /// <summary>
        /// Resolves a jsonElement representing an inner object based on the field's schema and metadata
        /// </summary>
        public JsonDocument ResolveInnerObject(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata);

        /// <summary>
        /// Resolves a jsonElement representing a list type based on the field's schema and metadata
        /// </summary>
        public IEnumerable<JsonDocument> ResolveListType(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata);

    }
}

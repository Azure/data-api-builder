using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using GraphQL.Execution;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Services
{
    //<summary>
    // SQLQueryEngine to Execute against SQL Db.
    //</summary>
    public class SQLQueryEngine : IQueryEngine
    {
        private readonly SQLClientProvider _clientProvider;
        private IMetadataStoreProvider _metadataStoreProvider;

        // <summary>
        // Constructor.
        // </summary>
        public SQLQueryEngine(IClientProvider<SqlConnection> clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            _clientProvider = (SQLClientProvider)clientProvider;
            _metadataStoreProvider = metadataStoreProvider;
        }

        // <summary>
        // Register the given resolver with this query engine.
        // </summary>
        public void RegisterResolver(GraphQLQueryResolver resolver)
        {
            _metadataStoreProvider.StoreQueryResolver(resolver);
        }

        // <summary>
        // Execute the given named graphql query on the backend.
        // </summary>
        public JsonDocument Execute(string graphQLQueryName, IDictionary<string, ArgumentValue> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            GraphQLQueryResolver resolver = _metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            SqlConnection conn = this._clientProvider.getClient();

            // TODO:
            // Open connection
            // Edit query = FOR JSON PATH
            // Execute Query
            // Parse Results into Json and return.
            // Will this work with multiple simultaneous calls ?

            JsonDocument jsonDocument = JsonDocument.Parse("{}");

            return jsonDocument;
        }

        // <summary>
        // Executes the given named graphql query on the backend and expecting a list of Jsons back.
        // </summary>
        public IEnumerable<JsonDocument> ExecuteList(string graphQLQueryName, IDictionary<string, ArgumentValue> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            var resolver = _metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            List<JsonDocument> resultsAsList = new List<JsonDocument>();
            return resultsAsList;
        }

        // <summary>
        // Returns if the given query is a list query.
        // </summary>
        public bool IsListQuery(string queryName)
        {
            return _metadataStoreProvider.GetQueryResolver(queryName).isList;
        }
    }
}

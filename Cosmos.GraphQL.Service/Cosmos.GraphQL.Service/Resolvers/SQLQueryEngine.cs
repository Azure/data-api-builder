using System;
using System.Collections.Generic;
using System.Data.Common;
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
using Microsoft.Sql.Rest.QueryHandler;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Services
{
    //<summary>
    // SQLQueryEngine to ExecuteAsync against SQL Db.
    //</summary>
    public class SQLQueryEngine : IQueryEngine
    {
        private IMetadataStoreProvider _metadataStoreProvider;
        private readonly IQueryExecutor _queryExecutor;

        private const string x_ForJsonSuffix = " FOR JSON PATH, ROOT('data');";

        // <summary>
        // Constructor.
        // </summary>
        public SQLQueryEngine(IMetadataStoreProvider metadataStoreProvider, IQueryExecutor queryExecutor)
        {
            _metadataStoreProvider = metadataStoreProvider;
            _queryExecutor = queryExecutor;
        }

        // <summary>
        // Register the given resolver with this query engine.
        // </summary>
        public void RegisterResolver(GraphQLQueryResolver resolver)
        {
            // Registration of Resolvers is already done at startup.
            // _metadataStoreProvider.StoreQueryResolver(resolver);
        }

        // <summary>
        // ExecuteAsync the given named graphql query on the backend.
        // </summary>
        public async Task<JsonDocument> ExecuteAsync(string graphQLQueryName, IDictionary<string, ArgumentValue> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another table
            // TODO: add support for TOP and Order-by push-down

            GraphQLQueryResolver resolver = _metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            JsonDocument jsonDocument = JsonDocument.Parse("{ }");
            try
            {
                // Edit query to add FOR JSON PATH
                //
                string queryText = resolver.parametrizedQuery + x_ForJsonSuffix;

                // Open connection and execute query using _queryExecutor
                //
                DbDataReader dbDataReader =  await _queryExecutor.ExecuteQueryAsync(queryText, resolver.databaseName);

                // Parse Results into Json and return
                // TODO: Is this sufficient for multiple rows in data ?
                //
                if (dbDataReader.FieldCount > 0)
                {
                    jsonDocument = JsonDocument.Parse(dbDataReader.GetString(0));
                }
                else
                {
                    Console.WriteLine("Did not return enough columns in the JSON result.");
                }
            }
            catch (SystemException ex)
            {
                Console.WriteLine("Caught an exception: " + ex.Message);
            }

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

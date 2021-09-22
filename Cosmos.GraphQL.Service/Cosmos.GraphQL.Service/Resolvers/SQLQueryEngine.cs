﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Cosmos.GraphQL.Services
{
    //<summary>
    // SqlQueryEngine to ExecuteAsync against Sql Db.
    //</summary>
    public class SqlQueryEngine : IQueryEngine
    {
        private IMetadataStoreProvider _metadataStoreProvider;
        private readonly IQueryExecutor _queryExecutor;

        private const string x_ForJsonSuffix = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string x_WithoutArrayWrapperSuffix = "WITHOUT_ARRAY_WRAPPER";

        // <summary>
        // Constructor.
        // </summary>
        public SqlQueryEngine(IMetadataStoreProvider metadataStoreProvider, IQueryExecutor queryExecutor)
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
            // no-op
        }

        // <summary>
        // ExecuteAsync the given named graphql query on the backend.
        // </summary>
        public async Task<JsonDocument> ExecuteAsync(string graphQLQueryName, IDictionary<string, object> parameters)
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
                string queryText = resolver.parametrizedQuery + x_ForJsonSuffix + "," + x_WithoutArrayWrapperSuffix + ";";
                List<IDataParameter> queryParameters = new List<IDataParameter>();

                if (parameters != null)
                {
                    foreach (var parameterEntry in parameters)
                    {
                        queryParameters.Add(new SqlParameter("@" + parameterEntry.Key, parameterEntry.Value));
                    }
                }

                // Open connection and execute query using _queryExecutor
                //
                DbDataReader dbDataReader =  await _queryExecutor.ExecuteQueryAsync(queryText, resolver.databaseName, queryParameters);

                // Parse Results into Json and return
                //
                if (await dbDataReader.ReadAsync())
                {
                    jsonDocument = JsonDocument.Parse(dbDataReader.GetString(0));
                }
                else
                {
                    Console.WriteLine("Did not return enough rows in the JSON result.");
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
        public async Task<IEnumerable<JsonDocument>> ExecuteListAsync(string graphQLQueryName, IDictionary<string, object> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            GraphQLQueryResolver resolver = _metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            List<JsonDocument> resultsAsList = new List<JsonDocument>();
            try
            {
                // Edit query to add FOR JSON PATH
                //
                string queryText = resolver.parametrizedQuery + x_ForJsonSuffix + ";";
                List<IDataParameter> queryParameters = new List<IDataParameter>();

                if (parameters != null)
                {
                    foreach (var parameterEntry in parameters)
                    {
                        queryParameters.Add(new SqlParameter("@" + parameterEntry.Key, parameterEntry.Value));
                    }
                }

                // Open connection and execute query using _queryExecutor
                //
                DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(queryText, resolver.databaseName, queryParameters);

                // Deserialize results into list of JsonDocuments and return
                //
                if (await dbDataReader.ReadAsync())
                {
                    resultsAsList = JsonSerializer.Deserialize<List<JsonDocument>>(dbDataReader.GetString(0));
                }
                else
                {
                    Console.WriteLine("Did not return enough rows in the JSON result.");
                }
            }
            catch (SystemException ex)
            {
                Console.WriteLine("Caught an exception: " + ex.Message);
            }

            return resultsAsList;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using GraphQL.Execution;
using System.Data;
using Cosmos.GraphQL.Services;

namespace Cosmos.GraphQL.Service.Resolvers
{
    //<summary>
    // SqlQueryEngine to ExecuteAsync against Sql Db.
    //</summary>
    public class SqlQueryEngine<ParameterT> : IQueryEngine
        where ParameterT: IDataParameter, new()
    {
        private IMetadataStoreProvider _metadataStoreProvider;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IQueryBuilder _queryBuilder;

        // <summary>
        // Constructor.
        // </summary>
        public SqlQueryEngine(IMetadataStoreProvider metadataStoreProvider, IQueryExecutor queryExecutor, IQueryBuilder queryBuilder)
        {
            _metadataStoreProvider = metadataStoreProvider;
            _queryExecutor = queryExecutor;
            _queryBuilder = queryBuilder;
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

            string queryText = _queryBuilder.Build(resolver.parametrizedQuery, false);
            List<IDataParameter> queryParameters = new List<IDataParameter>();

            if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    var parameter = new ParameterT();
                    parameter.ParameterName = "@" + parameterEntry.Key;
                    parameter.Value = parameterEntry.Value.Value;
                    queryParameters.Add(parameter);
                }
            }

            // Open connection and execute query using _queryExecutor
            //
            DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(queryText, resolver.databaseName, queryParameters);

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

            return jsonDocument;
        }

        // <summary>
        // Executes the given named graphql query on the backend and expecting a list of Jsons back.
        // </summary>
        public async Task<IEnumerable<JsonDocument>> ExecuteListAsync(string graphQLQueryName, IDictionary<string, ArgumentValue> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            GraphQLQueryResolver resolver = _metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            List<JsonDocument> resultsAsList = new List<JsonDocument>();
            // Edit query to add FOR JSON PATH
            //
            string queryText = _queryBuilder.Build(resolver.parametrizedQuery, true);
            List<IDataParameter> queryParameters = new List<IDataParameter>();

            if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    var parameter = new ParameterT();
                    parameter.ParameterName = "@" + parameterEntry.Key;
                    parameter.Value = parameterEntry.Value.Value;
                    queryParameters.Add(parameter);
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

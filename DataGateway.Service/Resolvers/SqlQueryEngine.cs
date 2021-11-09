using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using HotChocolate.Resolvers;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Resolvers
{
    //<summary>
    // SqlQueryEngine to ExecuteAsync against Sql Db.
    //</summary>
    public class SqlQueryEngine : IQueryEngine
    {
        private readonly IMetadataStoreProvider _metadataStoreProvider;
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
            // no-op
        }

        private static async Task<string> GetJsonStringFromDbReader(DbDataReader dbDataReader)
        {
            var jsonString = new StringBuilder();
            // Even though we only return a single cell, we need this loop for
            // MS SQL. Sadly it splits FOR JSON PATH output across multiple
            // cells if the JSON consists of more than 2033 bytes:
            // Sources:
            // 1. https://docs.microsoft.com/en-us/sql/relational-databases/json/format-query-results-as-json-with-for-json-sql-server?view=sql-server-2017#output-of-the-for-json-clause
            // 2. https://stackoverflow.com/questions/54973536/for-json-path-results-in-ssms-truncated-to-2033-characters/54973676
            // 3. https://docs.microsoft.com/en-us/sql/relational-databases/json/use-for-json-output-in-sql-server-and-in-client-apps-sql-server?view=sql-server-2017#use-for-json-output-in-a-c-client-app
            if (await dbDataReader.ReadAsync())
            {
                jsonString.Append(dbDataReader.GetString(0));
            }

            return jsonString.ToString();
        }

        // <summary>
        // ExecuteAsync the given named graphql query on the backend.
        // </summary>
        public async Task<JsonDocument> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another table
            // TODO: add support for TOP and Order-by push-down

            SqlQueryStructure structure = new(context, _metadataStoreProvider, _queryBuilder);
            Console.WriteLine(structure.ToString());
            // Open connection and execute query using _queryExecutor
            //
            DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(structure.ToString(), parameters);

            // Parse Results into Json and return
            if (!dbDataReader.HasRows)
            {
                return null;
            }

            return JsonDocument.Parse(await GetJsonStringFromDbReader(dbDataReader));
        }

        // <summary>
        // Executes the given named graphql query on the backend and expecting a list of Jsons back.
        // </summary>
        public async Task<IEnumerable<JsonDocument>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            SqlQueryStructure structure = new(context, _metadataStoreProvider, _queryBuilder);
            Console.WriteLine(structure.ToString());
            DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(structure.ToString(), parameters);

            // Parse Results into Json and return
            //
            if (!dbDataReader.HasRows)
            {
                return new List<JsonDocument>();
            }

            return JsonSerializer.Deserialize<List<JsonDocument>>(await GetJsonStringFromDbReader(dbDataReader));
        }
    }
}

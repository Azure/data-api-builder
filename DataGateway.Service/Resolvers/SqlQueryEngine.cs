using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Services;
using HotChocolate.Resolvers;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    //<summary>
    // SqlQueryEngine to execute queries against Sql like databases.
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

        public static async Task<string> GetJsonStringFromDbReader(DbDataReader dbDataReader)
        {
            StringBuilder jsonString = new();
            // Even though we only return a single cell, we need this loop for
            // MS SQL. Sadly it splits FOR JSON PATH output across multiple
            // cells if the JSON consists of more than 2033 bytes:
            // Sources:
            // 1. https://docs.microsoft.com/en-us/sql/relational-databases/json/format-query-results-as-json-with-for-json-sql-server?view=sql-server-2017#output-of-the-for-json-clause
            // 2. https://stackoverflow.com/questions/54973536/for-json-path-results-in-ssms-truncated-to-2033-characters/54973676
            // 3. https://docs.microsoft.com/en-us/sql/relational-databases/json/use-for-json-output-in-sql-server-and-in-client-apps-sql-server?view=sql-server-2017#use-for-json-output-in-a-c-client-app
            while (await dbDataReader.ReadAsync())
            {
                jsonString.Append(dbDataReader.GetString(0));
            }

            return jsonString.ToString();
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json back.
        /// </summary>
        public async Task<JsonDocument> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters, bool isContinuationQuery)
        {
            SqlQueryStructure structure = new(context, parameters, _metadataStoreProvider, _queryBuilder);
            return await ExecuteAsync(structure);
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL and expecting a
        /// list of Jsons back.
        /// </summary>
        public async Task<IEnumerable<JsonDocument>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            SqlQueryStructure structure = new(context, parameters, _metadataStoreProvider, _queryBuilder);
            Console.WriteLine(structure.ToString());
            using DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(structure.ToString(), structure.Parameters);

            // Parse Results into Json and return
            //
            if (!dbDataReader.HasRows)
            {
                return new List<JsonDocument>();
            }

            return JsonSerializer.Deserialize<List<JsonDocument>>(await GetJsonStringFromDbReader(dbDataReader));
        }

        // <summary>
        // Given the FindRequestContext, obtains the query text and executes it against the backend. Useful for REST API scenarios.
        // </summary>
        public async Task<JsonDocument> ExecuteAsync(RequestContext context)
        {
            SqlQueryStructure structure = new(context, _metadataStoreProvider, _queryBuilder);
            return await ExecuteAsync(structure);
        }

        // <summary>
        // Given the SqlQueryStructure structure, obtains the query text and executes it against the backend. Useful for REST API scenarios.
        // </summary>
        private async Task<JsonDocument> ExecuteAsync(SqlQueryStructure structure)
        {
            // Open connection and execute query using _queryExecutor
            //
            Console.WriteLine(structure.ToString());
            using DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(structure.ToString(), structure.Parameters);
            JsonDocument jsonDocument = null;

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
    }
}

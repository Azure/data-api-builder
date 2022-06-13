#nullable disable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Resolvers
{
    //<summary>
    // SqlQueryEngine to execute queries against Sql like databases.
    //</summary>
    public class SqlQueryEngine : IQueryEngine
    {
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IQueryBuilder _queryBuilder;

        // <summary>
        // Constructor.
        // </summary>
        public SqlQueryEngine(
            IQueryExecutor queryExecutor,
            IQueryBuilder queryBuilder,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            _queryExecutor = queryExecutor;
            _queryBuilder = queryBuilder;
            _sqlMetadataProvider = sqlMetadataProvider;
        }

        public static async Task<string> GetJsonStringFromDbReader(DbDataReader dbDataReader, IQueryExecutor executor)
        {
            StringBuilder jsonString = new();
            // Even though we only return a single cell, we need this loop for
            // MS SQL. Sadly it splits FOR JSON PATH output across multiple
            // cells if the JSON consists of more than 2033 bytes:
            // Sources:
            // 1. https://docs.microsoft.com/en-us/sql/relational-databases/json/format-query-results-as-json-with-for-json-sql-server?view=sql-server-2017#output-of-the-for-json-clause
            // 2. https://stackoverflow.com/questions/54973536/for-json-path-results-in-ssms-truncated-to-2033-characters/54973676
            // 3. https://docs.microsoft.com/en-us/sql/relational-databases/json/use-for-json-output-in-sql-server-and-in-client-apps-sql-server?view=sql-server-2017#use-for-json-output-in-a-c-client-app
            while (await executor.ReadAsync(dbDataReader))
            {
                jsonString.Append(dbDataReader.GetString(0));
            }

            return jsonString.ToString();
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json and its related pagination metadata back.
        /// </summary>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            SqlQueryStructure structure = new(context, parameters, _sqlMetadataProvider);

            if (structure.PaginationMetadata.IsPaginated)
            {
                return new Tuple<JsonDocument, IMetadata>(
                    SqlPaginationUtil.CreatePaginationConnectionFromJsonDocument(await ExecuteAsync(structure), structure.PaginationMetadata),
                    structure.PaginationMetadata);
            }
            else
            {
                return new Tuple<JsonDocument, IMetadata>(
                    await ExecuteAsync(structure),
                    structure.PaginationMetadata);
            }
        }

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL and expecting a
        /// list of Jsons and the relevant pagination metadata back.
        /// </summary>
        public async Task<Tuple<IEnumerable<JsonDocument>, IMetadata>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            SqlQueryStructure structure = new(context, parameters, _sqlMetadataProvider);
            string queryString = _queryBuilder.Build(structure);
            Console.WriteLine(queryString);
            using DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(queryString, structure.Parameters);

            // Parse Results into Json and return
            //
            if (!dbDataReader.HasRows)
            {
                return new Tuple<IEnumerable<JsonDocument>, IMetadata>(new List<JsonDocument>(), null);
            }

            return new Tuple<IEnumerable<JsonDocument>, IMetadata>(
                JsonSerializer.Deserialize<List<JsonDocument>>(await GetJsonStringFromDbReader(dbDataReader, _queryExecutor)),
                structure.PaginationMetadata
            );
        }

        // <summary>
        // Given the FindRequestContext, obtains the query text and executes it against the backend. Useful for REST API scenarios.
        // </summary>
        public async Task<JsonDocument> ExecuteAsync(RestRequestContext context)
        {
            SqlQueryStructure structure = new(context, _sqlMetadataProvider);
            JsonDocument queryJson = await ExecuteAsync(structure);
            // queryJson is null if dbreader had no rows to return
            // If no rows/empty table, return an empty json array
            return queryJson is null ? JsonDocument.Parse("[]") : queryJson;
        }

        /// <inheritdoc />
        public JsonDocument ResolveInnerObject(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata)
        {
            PaginationMetadata parentMetadata = (PaginationMetadata)metadata;
            PaginationMetadata currentMetadata = parentMetadata.Subqueries[fieldSchema.Name.Value];
            metadata = currentMetadata;

            if (currentMetadata.IsPaginated)
            {
                return SqlPaginationUtil.CreatePaginationConnectionFromJsonElement(element, currentMetadata);
            }
            else
            {
                //TODO: Try to avoid additional deserialization/serialization here.
                return ResolverMiddleware.RepresentsNullValue(element) ? null : JsonDocument.Parse(element.ToString());
            }
        }

        /// <inheritdoc />
        public IEnumerable<JsonDocument> ResolveListType(JsonElement element, IObjectField fieldSchema, ref IMetadata metadata)
        {
            PaginationMetadata parentMetadata = (PaginationMetadata)metadata;
            PaginationMetadata currentMetadata = parentMetadata.Subqueries[fieldSchema.Name.Value];
            metadata = currentMetadata;

            //TODO: Try to avoid additional deserialization/serialization here.
            return JsonSerializer.Deserialize<List<JsonDocument>>(element.ToString());
        }

        // <summary>
        // Given the SqlQueryStructure structure, obtains the query text and executes it against the backend. Useful for REST API scenarios.
        // </summary>
        private async Task<JsonDocument> ExecuteAsync(SqlQueryStructure structure)
        {
            // Open connection and execute query using _queryExecutor
            //
            string queryString = _queryBuilder.Build(structure);
            Console.WriteLine(queryString);
            using DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(queryString, structure.Parameters);
            JsonDocument jsonDocument = null;

            // Parse Results into Json and return
            //
            if (dbDataReader.HasRows)
            {
                // Make sure to get the complete json string in case of large document.
                jsonDocument =
                    JsonSerializer.Deserialize<JsonDocument>(
                        await GetJsonStringFromDbReader(dbDataReader, _queryExecutor));
            }
            else
            {
                Console.WriteLine("Did not return enough rows in the JSON result.");
            }

            return jsonDocument;
        }
    }
}

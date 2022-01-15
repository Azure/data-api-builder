using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Implements the mutation engine interface for mutations against Sql like databases.
    /// </summary>
    public class SqlMutationEngine : IMutationEngine
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMetadataStoreProvider _metadataStoreProvider;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IQueryBuilder _queryBuilder;

        /// <summary>
        /// Constructor
        /// </summary>
        public SqlMutationEngine(IQueryEngine queryEngine, IMetadataStoreProvider metadataStoreProvider, IQueryExecutor queryExecutor, IQueryBuilder queryBuilder)
        {
            _queryEngine = queryEngine;
            _metadataStoreProvider = metadataStoreProvider;
            _queryExecutor = queryExecutor;
            _queryBuilder = queryBuilder;
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<JsonDocument> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            if (context.Selection.Type.IsListType())
            {
                throw new NotSupportedException("Returning list types from mutations not supported");
            }

            string graphqlMutationName = context.Selection.Field.Name.Value;
            MutationResolver mutationResolver = _metadataStoreProvider.GetMutationResolver(graphqlMutationName);

            string tableName = mutationResolver.Table;
            TableDefinition tableDefinition = _metadataStoreProvider.GetTableDefinition(tableName);

            string queryString;
            Dictionary<string, object> queryParameters;

            switch (mutationResolver.OperationType)
            {
                case "INSERT":
                    SqlInsertStructure insertQueryStruct = new(tableName, tableDefinition, parameters, _queryBuilder);
                    queryString = insertQueryStruct.ToString();
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case "UPDATE":
                    SqlUpdateStructure updateQueryStruct = new(tableName, tableDefinition, parameters, _queryBuilder);
                    queryString = updateQueryStruct.ToString();
                    queryParameters = updateQueryStruct.Parameters;
                    break;
                default:
                    throw new Exception($"Unexpected value for MutationResolver.OperationType \"{mutationResolver.OperationType}\" found.");
            }

            Console.WriteLine(queryString);

            using DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(queryString, queryParameters);

            // scalar type return for mutation not supported / not useful
            // nothing to query
            if (context.Selection.Type.IsScalarType())
            {
                return null;
            }

            Dictionary<string, object> searchParams = await ExtractRowFromDbDataReader(dbDataReader);

            if (searchParams == null)
            {
                string searchedPK = '<' + string.Join(", ", tableDefinition.PrimaryKey.Select(pk => $"{pk}: {parameters[pk]}")) + '>';
                throw new DatagatewayException($"Could not find entity with {searchedPK}", 404, DatagatewayException.SubStatusCodes.EntityNotFound);
            }

            // delegates the querying part of the mutation to the QueryEngine
            // this allows for nested queries in muatations
            // the searchParams are used to identify the mutated record so it can then be further queried on
            return await _queryEngine.ExecuteAsync(context, searchParams, false);
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<JsonDocument> ExecuteAsync(RequestContext context)
        {
            TableDefinition tableDefinition = _metadataStoreProvider.GetTableDefinition(context.EntityName);

            string queryString;
            Dictionary<string, object> queryParameters;

            switch (context.OperationType)
            {
                case Operation.Create:
                    SqlInsertStructure insertQueryStruct =
                        new(context.EntityName, tableDefinition, context.FieldValuePairs, _queryBuilder);
                    queryString = insertQueryStruct.ToString();
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                default:
                    throw new DatagatewayException(
                        message: $"Unexpected DML operation \" {context.OperationType}\" requested.",
                        statusCode: (int)HttpStatusCode.BadRequest,
                        subStatusCode: DatagatewayException.SubStatusCodes.BadRequest)
                    ;
            }

            Console.WriteLine(queryString);

            using DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(queryString, queryParameters);

            context.FieldValuePairs = await ExtractRowFromDbDataReader(dbDataReader);

            if (context.FieldValuePairs == null)
            {
                throw new DatagatewayException(
                    message: $"Could not perform the given request on entity {context.EntityName}",
                    statusCode: (int)HttpStatusCode.InternalServerError,
                    subStatusCode: DatagatewayException.SubStatusCodes.DatabaseOperationFailed);
            }

            // delegates the querying part of the mutation to the QueryEngine
            // this allows for nested queries in muatations
            // the searchParams are used to identify the mutated record so it can then be further queried on
            return await _queryEngine.ExecuteAsync(context);
        }

        ///<summary>
        /// Extracts a single row from DbDataReader and format it so it can be used as a parameter to a query execution
        ///</summary>
        ///<returns>A dictionary representating the row in <c>ColumnName: Value</c> format, null if no row was found</returns>
        private static async Task<Dictionary<string, object>> ExtractRowFromDbDataReader(DbDataReader dbDataReader)
        {
            Dictionary<string, object> row = new();

            if (await dbDataReader.ReadAsync())
            {
                if (dbDataReader.HasRows)
                {
                    DataTable schemaTable = dbDataReader.GetSchemaTable();

                    foreach (DataRow schemaRow in schemaTable.Rows)
                    {
                        string columnName = (string)schemaRow["ColumnName"];
                        row.Add(columnName, dbDataReader[columnName]);
                    }
                }
            }

            // no row was read
            if (row.Count == 0)
            {
                return null;
            }

            return row;
        }
    }
}

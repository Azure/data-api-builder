using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
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
        /// <returns>JSON object result and its related pagination metadata</returns>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
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

            Tuple<JsonDocument, IMetadata> result = new(null, null);

            switch (mutationResolver.OperationType)
            {
                case MutationOperation.Insert:
                    SqlInsertStructure insertQueryStruct = new(tableName, tableDefinition, parameters);
                    queryString = _queryBuilder.Build(insertQueryStruct);
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case MutationOperation.Update:
                    SqlUpdateStructure updateQueryStruct = new(tableName, tableDefinition, parameters);
                    queryString = _queryBuilder.Build(updateQueryStruct);
                    queryParameters = updateQueryStruct.Parameters;
                    break;
                case MutationOperation.Delete:
                    // compute the mutation result before removing the element
                    result = await _queryEngine.ExecuteAsync(context, parameters, false);
                    SqlDeleteStructure deleteStructure = new(tableName, tableDefinition, parameters);
                    queryString = _queryBuilder.Build(deleteStructure);
                    queryParameters = deleteStructure.Parameters;
                    break;
                default:
                    throw new NotSupportedException($"Unexpected value for MutationResolver.OperationType \"{mutationResolver.OperationType}\" found.");
            }

            Console.WriteLine(queryString);

            using DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(queryString, queryParameters);

            if (!context.Selection.Type.IsScalarType() && mutationResolver.OperationType != MutationOperation.Delete)
            {
                Dictionary<string, object> searchParams = await ExtractRowFromDbDataReader(dbDataReader);

                if (searchParams == null)
                {
                    string searchedPK = '<' + string.Join(", ", tableDefinition.PrimaryKey.Select(pk => $"{pk}: {parameters[pk]}")) + '>';
                    throw new DatagatewayException($"Could not find entity with {searchedPK}", 404, DatagatewayException.SubStatusCodes.EntityNotFound);
                }

                result = await _queryEngine.ExecuteAsync(context, searchParams, false);
            }

            return result;
        }

        ///<summary>
        /// Extracts a single row from DbDataReader and format it so it can be used as a paramter to a query execution
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

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

            Tuple<JsonDocument, IMetadata> result = new(null, null);

            if (mutationResolver.OperationType == Operation.Delete)
            {
                // compute the mutation result before removing the element
                result = await _queryEngine.ExecuteAsync(
                    context,
                    parameters,
                    isPaginationQuery: false);
            }

            using DbDataReader dbDataReader =
                await PerformMutationOperation(
                    tableName,
                    mutationResolver.OperationType,
                    parameters);

            if (!context.Selection.Type.IsScalarType() && mutationResolver.OperationType != Operation.Delete)
            {
                Dictionary<string, object> searchParams = await ExtractRowFromDbDataReader(dbDataReader);

                if (searchParams == null)
                {
                    TableDefinition tableDefinition = _metadataStoreProvider.GetTableDefinition(tableName);
                    string searchedPK = '<' + string.Join(", ", tableDefinition.PrimaryKey.Select(pk => $"{pk}: {parameters[pk]}")) + '>';
                    throw new DatagatewayException($"Could not find entity with {searchedPK}", 404, DatagatewayException.SubStatusCodes.EntityNotFound);
                }

                result = await _queryEngine.ExecuteAsync(
                    context,
                    searchParams,
                    isPaginationQuery: false);
            }

            return result;
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<JsonDocument> ExecuteAsync(RestRequestContext context)
        {
            Dictionary<string, object> parameters = PrepareParameters(context);

            try
            {
                if (context.OperationType == Operation.Upsert)
                {
                    using DbDataReader dbDataReader2 =
                        await PerformMutationOperationNonQuery(
                        context.EntityName,
                        context.OperationType,
                        parameters);
                    Tuple<bool, Dictionary<string,object>> recordUpdated = await ExtractChangesFromDbDataReader(dbDataReader2);

                    // If the record was not updated, then an Insert occurred.
                    if (!recordUpdated.Item1)
                    {
                        string resultString = JsonSerializer.Serialize(recordUpdated.Item2);
                        return JsonDocument.Parse(resultString);
                    }

                    // If record was updated, null signals upstream controller to return HTTP 204 No Content
                    return null;
                }

                using DbDataReader dbDataReader =
                await PerformMutationOperation(
                    context.EntityName,
                    context.OperationType,
                    parameters);
                context.PrimaryKeyValuePairs = await ExtractRowFromDbDataReader(dbDataReader);

                if (context.OperationType == Operation.Delete)
                {
                    // Records affected tells us that item was successfully deleted.
                    // No records affected happens for a DELETE request on nonexistent object
                    // Returning empty JSON result triggers a NoContent result in calling REST service.
                    if (dbDataReader.RecordsAffected > 0)
                    {
                        return JsonDocument.Parse("{}");
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch (DbException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                throw new DatagatewayException(
                    message: $"Could not perform the given mutation on entity {context.EntityName}.",
                    statusCode: (int)HttpStatusCode.InternalServerError,
                    subStatusCode: DatagatewayException.SubStatusCodes.DatabaseOperationFailed);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
            }

            // Reuse the same context as a FindRequestContext to return the results after the mutation operation.
            context.OperationType = Operation.Find;

            // delegates the querying part of the mutation to the QueryEngine
            return await _queryEngine.ExecuteAsync(context);
        }

        /// <summary>
        /// Performs the given mutation operation type on the table and
        /// returns result as JSON object asynchronously.
        /// </summary>
        private async Task<DbDataReader> PerformMutationOperation(
            string tableName,
            Operation operationType,
            IDictionary<string, object> parameters)
        {
            string queryString;
            Dictionary<string, object> queryParameters;

            switch (operationType)
            {
                case Operation.Insert:
                    SqlInsertStructure insertQueryStruct = new(tableName, _metadataStoreProvider, parameters);
                    queryString = _queryBuilder.Build(insertQueryStruct);
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case Operation.Update:
                    SqlUpdateStructure updateQueryStruct = new(tableName, _metadataStoreProvider, parameters);
                    queryString = _queryBuilder.Build(updateQueryStruct);
                    queryParameters = updateQueryStruct.Parameters;
                    break;
                case Operation.Delete:
                    SqlDeleteStructure deleteStructure = new(tableName, _metadataStoreProvider, parameters);
                    queryString = _queryBuilder.Build(deleteStructure);
                    queryParameters = deleteStructure.Parameters;
                    break;
                default:
                    throw new NotSupportedException($"Unexpected mutation operation \" {operationType}\" requested.");
            }

            Console.WriteLine(queryString);

            return await _queryExecutor.ExecuteQueryAsync(queryString, queryParameters);
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

        ///<summary>
        /// Processes multiple result sets from DbDataReader and format it so it can be used as a parameter to a query execution.
        /// In upsert:
        /// result set #1: result of the UPDATE operation.
        /// result set #2: result of the INSERT operation.
        ///</summary>
        ///<returns>A dictionary representing the full object modified or inserted.</returns>
        private static async Task<Tuple<bool,Dictionary<string, object>>> ExtractChangesFromDbDataReader(DbDataReader dbDataReader)
        {
            Dictionary<string, object> row = new();

            // Do-While because first result set needs to be checked
            // as calling dbReader.NextResultAsync() would skip to next result set.
            int resultSetsFound = 0;
            do
            {
                // Result sets incremented here since dbDataReader.ReadAsync() may have no rows to read from
                // due to an emtpy result set.
                resultSetsFound++;

                while (await dbDataReader.ReadAsync())
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
                
            } while (await dbDataReader.NextResultAsync());

            bool updateOccurred = true;
            // Two result sets indicates Update failed and Insert performed instead.
            if (resultSetsFound > 1)
            {
                updateOccurred = false;
            }

            return new Tuple<bool, Dictionary<string, object>>(updateOccurred, new(row));
        }

        /// <summary>
        /// Performs the given mutation operation type on the table in a Transaction and
        /// returns result as JSON object asynchronously.
        /// </summary>
        private async Task<DbDataReader> PerformMutationOperationNonQuery(
            string tableName,
            Operation operationType,
            IDictionary<string, object> parameters)
        {
            string queryString;
            Dictionary<string, object> queryParameters;

            switch (operationType)
            {
                case Operation.Upsert:
                    SqlUpsertQueryStructure upsertStructure = new(tableName, _metadataStoreProvider, parameters);
                    queryString = _queryBuilder.Build(upsertStructure);
                    queryParameters = upsertStructure.Parameters;
                    break;
                default:
                    throw new NotSupportedException($"Unexpected mutation operation \" {operationType}\" requested.");
            }

            Console.WriteLine(queryString);
            return await _queryExecutor.ExecuteNonQueryAsync(queryString, queryParameters);
        }

        private static Dictionary<string, object> PrepareParameters(RestRequestContext context)
        {
            Dictionary<string, object> parameters;

            if (context.OperationType == Operation.Delete)
            {
                // DeleteOne based off primary key in request.
                parameters = new(context.PrimaryKeyValuePairs);
            }
            else if (context.OperationType == Operation.Upsert)
            {
                // Combine both PrimaryKey/Field ValuePairs
                // because we create both an insert and an update statement.
                parameters = new(context.PrimaryKeyValuePairs);
                foreach (KeyValuePair<string, object> pair in context.FieldValuePairsInBody)
                {
                    parameters.Add(pair.Key, pair.Value);
                }
            }
            else
            {
                parameters = new(context.FieldValuePairsInBody);
            }

            return parameters;
        }
    }
}

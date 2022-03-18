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
using Azure.DataGateway.Service.Services;
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
        /// Executes the GraphQL mutation query and returns result as JSON object asynchronously.
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

            Tuple<JsonDocument, IMetadata>? result = null;

            if (mutationResolver.OperationType == Operation.Delete)
            {
                // compute the mutation result before removing the element
                result = await _queryEngine.ExecuteAsync(
                    context,
                // Disabling the warning since trying to fix this opens up support for nullability
                // tracked in #235 on REST and #201 on GraphQL.
#pragma warning disable CS8620
                    parameters);
#pragma warning restore CS8620
            }

            using DbDataReader dbDataReader =
                await PerformMutationOperation(
                    tableName,
                    mutationResolver.OperationType,
                    parameters);

            if (!context.Selection.Type.IsScalarType() && mutationResolver.OperationType != Operation.Delete)
            {
                Dictionary<string, object?>? searchParams = await ExtractRowFromDbDataReader(dbDataReader);

                if (searchParams == null)
                {
                    TableDefinition tableDefinition = _metadataStoreProvider.GetTableDefinition(tableName);
                    string searchedPK = '<' + string.Join(", ", tableDefinition.PrimaryKey.Select(pk => $"{pk}: {parameters[pk]}")) + '>';
                    throw new DataGatewayException($"Could not find entity with {searchedPK}", HttpStatusCode.NotFound, DataGatewayException.SubStatusCodes.EntityNotFound);
                }

                result = await _queryEngine.ExecuteAsync(
                    context,
                    searchParams);
            }

            if (result == null)
            {
                throw new DataGatewayException("Failed to resolve any query based on the current configuration.", HttpStatusCode.BadRequest, DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            return result;
        }

        /// <summary>
        /// Executes the REST mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<JsonDocument?> ExecuteAsync(RestRequestContext context)
        {
            Dictionary<string, object> parameters = PrepareParameters(context);

            try
            {
                using DbDataReader dbDataReader =
                await PerformMutationOperation(
                    context.EntityName,
                    context.OperationType,
                    parameters);

                Dictionary<string, object?>? resultRecord = new();
                resultRecord = await ExtractRowFromDbDataReader(dbDataReader);

                string? jsonResultString = null;

                switch (context.OperationType)
                {
                    case Operation.Delete:
                        // Records affected tells us that item was successfully deleted.
                        // No records affected happens for a DELETE request on nonexistent object
                        // Returning empty JSON result triggers a NoContent result in calling REST service.
                        if (dbDataReader.RecordsAffected > 0)
                        {
                            jsonResultString = "{}";
                        }

                        break;

                    case Operation.Insert:
                    case Operation.Update:
                    case Operation.UpdateIncremental:
                        jsonResultString = null;
                        ;
                        break;

                    case Operation.Upsert:
                    case Operation.UpsertIncremental:
                        /// Processes a second result set from DbDataReader if it exists.
                        /// In MsSQL upsert:
                        /// result set #1: result of the UPDATE operation.
                        /// result set #2: result of the INSERT operation.
                        if (resultRecord != null)
                        {
                            // We give empty result set for updates
                            jsonResultString = null;
                        }
                        else if (await dbDataReader.NextResultAsync())
                        {
                            // Since no first result set exists, we overwrite Dictionary here.
                            resultRecord = await ExtractRowFromDbDataReader(dbDataReader);
                            jsonResultString = JsonSerializer.Serialize(resultRecord);
                        }
                        else
                        {
                            // If there is no resultset, raise dbexception
                            // this is needed for MySQL.
                            throw new DataGatewayException(
                                message: $"Could not perform the given mutation on entity {context.EntityName}.",
                                statusCode: HttpStatusCode.InternalServerError,
                                subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed);
                        }

                        break;
                }

                if (jsonResultString == null)
                {
                    return null;
                }

                return JsonDocument.Parse(jsonResultString);
            }
            catch (DbException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);

                throw new DataGatewayException(
                    message: $"Could not perform the given mutation on entity {context.EntityName}.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed);
            }
        }

        /// <summary>
        /// Performs the given REST and GraphQL mutation operation type
        /// on the table and returns result as JSON object asynchronously.
        /// </summary>
        private async Task<DbDataReader> PerformMutationOperation(
            string tableName,
            Operation operationType,
            IDictionary<string, object> parameters)
        {
            string queryString;
            Dictionary<string, object?> queryParameters;

            switch (operationType)
            {
                case Operation.Insert:
                    SqlInsertStructure insertQueryStruct = new(tableName, _metadataStoreProvider, parameters);
                    queryString = _queryBuilder.Build(insertQueryStruct);
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case Operation.Update:
                case Operation.UpdateIncremental:
                    SqlUpdateStructure updateIncrementalStructure = new(tableName, _metadataStoreProvider, parameters);
                    queryString = _queryBuilder.Build(updateIncrementalStructure);
                    queryParameters = updateIncrementalStructure.Parameters;
                    break;
                case Operation.Delete:
                    SqlDeleteStructure deleteStructure = new(tableName, _metadataStoreProvider, parameters);
                    queryString = _queryBuilder.Build(deleteStructure);
                    queryParameters = deleteStructure.Parameters;
                    break;
                case Operation.Upsert:
                    SqlUpsertQueryStructure upsertStructure = new(tableName, _metadataStoreProvider, parameters, incrementalUpdate: false);
                    queryString = _queryBuilder.Build(upsertStructure);
                    queryParameters = upsertStructure.Parameters;
                    break;
                case Operation.UpsertIncremental:
                    SqlUpsertQueryStructure upsertIncrementalStructure = new(tableName, _metadataStoreProvider, parameters, incrementalUpdate: true);
                    queryString = _queryBuilder.Build(upsertIncrementalStructure);
                    queryParameters = upsertIncrementalStructure.Parameters;
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
        private static async Task<Dictionary<string, object?>?> ExtractRowFromDbDataReader(DbDataReader dbDataReader)
        {
            Dictionary<string, object?> row = new();

            if (await dbDataReader.ReadAsync())
            {
                if (dbDataReader.HasRows)
                {
                    DataTable? schemaTable = dbDataReader.GetSchemaTable();

                    if (schemaTable != null)
                    {
                        foreach (DataRow schemaRow in schemaTable.Rows)
                        {
                            string columnName = (string)schemaRow["ColumnName"];
                            int colIndex = dbDataReader.GetOrdinal(columnName);
                            if (!dbDataReader.IsDBNull(colIndex))
                            {
                                row.Add(columnName, dbDataReader[columnName]);
                            }
                            else
                            {
                                row.Add(columnName, value: null);
                            }
                        }
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

        private static Dictionary<string, object> PrepareParameters(RestRequestContext context)
        {
            Dictionary<string, object> parameters;

            if (context.OperationType == Operation.Delete)
            {
                // DeleteOne based off primary key in request.
                parameters = new(context.PrimaryKeyValuePairs);
            }
            else if (context.OperationType == Operation.Upsert ||
                     context.OperationType == Operation.UpsertIncremental ||
                     context.OperationType == Operation.Update ||
                     context.OperationType == Operation.UpdateIncremental)
            {
                // Combine both PrimaryKey/Field ValuePairs
                // because we create an update statement.
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

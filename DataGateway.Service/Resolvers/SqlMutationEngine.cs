using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Implements the mutation engine interface for mutations against Sql like databases.
    /// </summary>
    public class SqlMutationEngine : IMutationEngine
    {
        private readonly IQueryEngine _queryEngine;
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IQueryBuilder _queryBuilder;

        /// <summary>
        /// Constructor
        /// </summary>
        public SqlMutationEngine(
            IQueryEngine queryEngine,
            IQueryExecutor queryExecutor,
            IQueryBuilder queryBuilder,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            _queryEngine = queryEngine;
            _queryExecutor = queryExecutor;
            _queryBuilder = queryBuilder;
            _sqlMetadataProvider = sqlMetadataProvider;
        }

        /// <summary>
        /// Executes the GraphQL mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result and its related pagination metadata</returns>
        public async Task<Tuple<JsonDocument, IMetadata>> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> parameters)
        {
            if (context.Selection.Type.IsListType())
            {
                throw new NotSupportedException("Returning list types from mutations not supported");
            }

            string graphqlMutationName = context.Selection.Field.Name.Value;
            string entityName = context.Selection.Field.Type.TypeName();

            Tuple<JsonDocument, IMetadata>? result = null;
            Operation mutationOperation =
                MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);
            if (mutationOperation is Operation.Delete)
            {
                // compute the mutation result before removing the element
                result = await _queryEngine.ExecuteAsync(context, parameters);
            }

            using DbDataReader dbDataReader =
                await PerformMutationOperation(
                    entityName,
                    mutationOperation,
                    parameters);

            if (!context.Selection.Type.IsScalarType() && mutationOperation is not Operation.Delete)
            {
                TableDefinition tableDefinition = _sqlMetadataProvider.GetTableDefinition(entityName);

                // only extract pk columns
                // since non pk columns can be null
                // and the subsequent query would search with:
                // nullParamName = NULL
                // which would fail to get the mutated entry from the db
                Dictionary<string, object?>? searchParams = await _queryExecutor.ExtractRowFromDbDataReader(
                    dbDataReader,
                    onlyExtract: tableDefinition.PrimaryKey);

                if (searchParams == null)
                {
                    string searchedPK = '<' + string.Join(", ", tableDefinition.PrimaryKey.Select(pk => $"{pk}: {parameters[pk]}")) + '>';
                    throw new DataGatewayException(
                        message: $"Could not find entity with {searchedPK}",
                        statusCode: HttpStatusCode.NotFound,
                        subStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound);
                }

                result = await _queryEngine.ExecuteAsync(
                    context,
                    searchParams);
            }

            if (result is null)
            {
                throw new DataGatewayException(
                    message: "Failed to resolve any query based on the current configuration.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            return result;
        }

        /// <summary>
        /// Executes the REST mutation query and returns IActionResult asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <returns>IActionResult</returns>
        public async Task<IActionResult?> ExecuteAsync(RestRequestContext context)
        {
            Dictionary<string, object?> parameters = PrepareParameters(context);

            using DbDataReader dbDataReader =
            await PerformMutationOperation(
                context.EntityName,
                context.OperationType,
                parameters);

            string primaryKeyRoute;
            Dictionary<string, object?>? resultRecord = new();
            resultRecord = await _queryExecutor.ExtractRowFromDbDataReader(dbDataReader);

            switch (context.OperationType)
            {
                case Operation.Delete:
                    // Records affected tells us that item was successfully deleted.
                    // No records affected happens for a DELETE request on nonexistent object
                    if (dbDataReader.RecordsAffected > 0)
                    {
                        return new NoContentResult();
                    }

                    break;
                case Operation.Insert:
                    if (resultRecord is null)
                    {
                        // this case should not happen, we throw an exception
                        // which will be returned as an Unexpected Internal Server Error
                        throw new Exception();
                    }

                    primaryKeyRoute = ConstructPrimaryKeyRoute(context.EntityName, resultRecord);
                    // location will be updated in rest controller where httpcontext is available
                    return new CreatedResult(location: primaryKeyRoute, OkMutationResponse(resultRecord).Value);
                case Operation.Update:
                case Operation.UpdateIncremental:
                    // Nothing to update means we throw Exception
                    if (resultRecord is null || resultRecord.Count == 0)
                    {
                        throw new DataGatewayException(message: "No Update could be performed, record not found",
                                                       statusCode: HttpStatusCode.PreconditionFailed,
                                                       subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed);
                    }

                    // Valid REST updates return OkObjectResult
                    return OkMutationResponse(resultRecord);
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                    /// Processes a second result set from DbDataReader if it exists.
                    /// In MsSQL upsert:
                    /// result set #1: result of the UPDATE operation.
                    /// result set #2: result of the INSERT operation.
                    if (resultRecord is not null)
                    {
                        // postgress may be an insert op here, if so, return CreatedResult
                        if (_sqlMetadataProvider.GetDatabaseType() is DatabaseType.postgresql &&
                            PostgresQueryBuilder.IsInsert(resultRecord))
                        {
                            primaryKeyRoute = ConstructPrimaryKeyRoute(context.EntityName, resultRecord);
                            // location will be updated in rest controller where httpcontext is available
                            return new CreatedResult(location: primaryKeyRoute, OkMutationResponse(resultRecord).Value);
                        }

                        // Valid REST updates return OkObjectResult
                        return OkMutationResponse(resultRecord);
                    }
                    else if (await dbDataReader.NextResultAsync())
                    {
                        // Since no first result set exists, we overwrite Dictionary here.
                        resultRecord = await _queryExecutor.ExtractRowFromDbDataReader(dbDataReader);
                        if (resultRecord is null)
                        {
                            break;
                        }

                        // location will be updated in rest controller where httpcontext is available
                        primaryKeyRoute = ConstructPrimaryKeyRoute(context.EntityName, resultRecord);
                        return new CreatedResult(location: primaryKeyRoute, OkMutationResponse(resultRecord).Value);
                    }
                    else
                    {
                        string prettyPrintPk = "<" + string.Join(", ", context.PrimaryKeyValuePairs.Select(
                            kv_pair => $"{kv_pair.Key}: {kv_pair.Value}"
                        )) + ">";
                        throw new DataGatewayException(
                            message: $"Cannot perform INSERT and could not find {context.EntityName} " +
                                        $"with primary key {prettyPrintPk} to perform UPDATE on.",
                            statusCode: HttpStatusCode.NotFound,
                            subStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound);
                    }
            }

            // if we have not yet returned, record is null
            return null;
        }

        /// <summary>
        /// Helper function returns an OkObjectResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// </summary>
        /// <param name="result">Dictionary representing the results of the client's request.</param>
        private static OkObjectResult OkMutationResponse(Dictionary<string, object?> result)
        {
            // Convert Dictionary to array of JsonElements
            string jsonString = $"[{JsonSerializer.Serialize(result)}]";
            JsonElement jsonResult = JsonSerializer.Deserialize<JsonElement>(jsonString);
            IEnumerable<JsonElement> resultEnumerated = jsonResult.EnumerateArray();

            return new OkObjectResult(new
            {
                value = resultEnumerated
            });
        }

        /// <summary>
        /// Performs the given REST and GraphQL mutation operation type
        /// on the table and returns result as JSON object asynchronously.
        /// </summary>
        private async Task<DbDataReader> PerformMutationOperation(
            string entityName,
            Operation operationType,
            IDictionary<string, object?> parameters)
        {
            string queryString;
            Dictionary<string, object?> queryParameters;

            switch (operationType)
            {
                case Operation.Insert:
                case Operation.Create:
                    SqlInsertStructure insertQueryStruct =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters);
                    queryString = _queryBuilder.Build(insertQueryStruct);
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case Operation.Update:
                    SqlUpdateStructure updateStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters,
                        isIncrementalUpdate: false);
                    queryString = _queryBuilder.Build(updateStructure);
                    queryParameters = updateStructure.Parameters;
                    break;
                case Operation.UpdateIncremental:
                    SqlUpdateStructure updateIncrementalStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters,
                        isIncrementalUpdate: true);
                    queryString = _queryBuilder.Build(updateIncrementalStructure);
                    queryParameters = updateIncrementalStructure.Parameters;
                    break;
                case Operation.UpdateGraphQL:
                    SqlUpdateStructure updateGraphQLStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters);
                    queryString = _queryBuilder.Build(updateGraphQLStructure);
                    queryParameters = updateGraphQLStructure.Parameters;
                    break;
                case Operation.Delete:
                    SqlDeleteStructure deleteStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters);
                    queryString = _queryBuilder.Build(deleteStructure);
                    queryParameters = deleteStructure.Parameters;
                    break;
                case Operation.Upsert:
                    SqlUpsertQueryStructure upsertStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters,
                        incrementalUpdate: false);
                    queryString = _queryBuilder.Build(upsertStructure);
                    queryParameters = upsertStructure.Parameters;
                    break;
                case Operation.UpsertIncremental:
                    SqlUpsertQueryStructure upsertIncrementalStructure =
                        new(entityName,
                        _sqlMetadataProvider,
                        parameters,
                        incrementalUpdate: true);
                    queryString = _queryBuilder.Build(upsertIncrementalStructure);
                    queryParameters = upsertIncrementalStructure.Parameters;
                    break;
                default:
                    throw new NotSupportedException($"Unexpected mutation operation \" {operationType}\" requested.");
            }

            Console.WriteLine(queryString);

            return await _queryExecutor.ExecuteQueryAsync(queryString, queryParameters);
        }

        /// <summary>
        /// For the given entity, constructs the primary key route
        /// using the primary key names from metadata and their values
        /// from the JsonElement representing the entity.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="entity">A Json element representing one instance of the entity.</param>
        /// <remarks> This function expects the Json element entity to contain all the properties
        /// that make up the primary keys.</remarks>
        /// <returns>the primary key route e.g. /id/1/partition/2 where id and partition are primary keys.</returns>
        public string ConstructPrimaryKeyRoute(string entityName, Dictionary<string, object?> entity)
        {
            TableDefinition tableDefinition = _sqlMetadataProvider.GetTableDefinition(entityName);
            StringBuilder newPrimaryKeyRoute = new();

            foreach (string primaryKey in tableDefinition.PrimaryKey)
            {
                newPrimaryKeyRoute.Append(primaryKey);
                newPrimaryKeyRoute.Append("/");
                newPrimaryKeyRoute.Append(entity[primaryKey]!.ToString());
                newPrimaryKeyRoute.Append("/");
            }

            // Remove the trailing "/"
            newPrimaryKeyRoute.Remove(newPrimaryKeyRoute.Length - 1, 1);

            return newPrimaryKeyRoute.ToString();
        }

        private static Dictionary<string, object?> PrepareParameters(RestRequestContext context)
        {
            Dictionary<string, object?> parameters;

            switch (context.OperationType)
            {
                case Operation.Delete:
                    // DeleteOne based off primary key in request.
                    parameters = new(context.PrimaryKeyValuePairs!);
                    break;
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                case Operation.Update:
                case Operation.UpdateIncremental:
                    // Combine both PrimaryKey/Field ValuePairs
                    // because we create an update statement.
                    parameters = new(context.PrimaryKeyValuePairs!);
                    foreach (KeyValuePair<string, object?> pair in context.FieldValuePairsInBody)
                    {
                        parameters.Add(pair.Key, pair.Value);
                    }

                    break;
                default:
                    parameters = new(context.FieldValuePairsInBody);
                    break;
            }

            return parameters;
        }
    }
}

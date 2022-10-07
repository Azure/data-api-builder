using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Resolvers;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Class which validates input supplied by a REST request.
    /// </summary>
    public class RequestValidator
    {
        public const string REQUEST_BODY_INVALID_JSON_ERR_MESSAGE = "Request body contains invalid JSON.";
        public const string BATCH_MUTATION_UNSUPPORTED_ERR_MESSAGE = "A Mutation operation on more than one entity in a single request is not yet supported.";
        public const string QUERY_STRING_INVALID_USAGE_ERR_MESSAGE = "Query string for this HTTP request type is an invalid URL.";
        public const string PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE = "Primary Key for this HTTP request type is required.";
        private IQueryExecutor _queryExecutor;
        private RuntimeConfigProvider _runtimeConfigProvider;

        public RequestValidator(
            IQueryExecutor queryExecutor,
            RuntimeConfigProvider runtimeConfigProvider)
        {
            _queryExecutor = queryExecutor;
            _runtimeConfigProvider = runtimeConfigProvider;
        }
        /// <summary>
        /// Validates the given request by ensuring:
        /// - each field to be returned is one of the exposed names for the entity.
        /// - extra fields specified in the body, will be discarded.
        /// </summary>
        /// <param name="context">Request context containing the REST operation fields and their values.</param>
        /// <param name="sqlMetadataProvider">SqlMetadata provider that enables referencing DB schema.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void ValidateRequestContext(
            RestRequestContext context,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            foreach (string field in context.FieldsToBeReturned)
            {
                // Get backing column and check that column is valid
                if (!sqlMetadataProvider.TryGetBackingColumn(context.EntityName, field, out string? backingColumn) ||
                    !sqlMetadataProvider.GetTableDefinition(context.EntityName).Columns.ContainsKey(backingColumn!))
                {
                    throw new DataApiBuilderException(
                        message: "Invalid field to be returned requested: " + field,
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
        }

        /// <summary>
        /// Tries to validate the primary key in the request match those specified in the entity
        /// definition in the configuration file.
        /// </summary>
        /// <param name="context">Request context containing the primary keys and their values.</param>
        /// <param name="sqlMetadataProvider">To get the table definition.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void ValidatePrimaryKey(
            RestRequestContext context,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            TableDefinition baseTableDefinition = TryGetTableDefinition(context.BaseEntityName, sqlMetadataProvider);

            int countOfPrimaryKeysInSchema = baseTableDefinition.PrimaryKey.Count;
            int countOfPrimaryKeysInRequest = context.PrimaryKeyValuePairs.Count;

            if (countOfPrimaryKeysInRequest < countOfPrimaryKeysInSchema)
            {
                throw new DataApiBuilderException(
                    message: "Primary key column(s) provided do not match DB schema.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            List<string> primaryKeysInRequest = new();
            foreach (string pk in context.PrimaryKeyValuePairs.Keys)
            {
                if (!sqlMetadataProvider.TryGetBackingColumn(context.EntityName, pk, out string? backingColumn))
                {
                    throw new DataApiBuilderException(
                    message: $"Primary key column: {pk} not found in the entity definition.",
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
                }

                primaryKeysInRequest.Add(backingColumn!);
            }

            // Verify each primary key is present in the table definition.

            List<string> primaryKeysInBaseTable = new();

            foreach (string primarKey in baseTableDefinition.PrimaryKey)
            {
                string primaryKeyAlias = context.ColumnAliases.ContainsKey(primarKey) ?
                    context.ColumnAliases[primarKey] : primarKey;

                sqlMetadataProvider.TryGetExposedColumnName(context.EntityName, primaryKeyAlias, out string? exposedPrimaryKeyName);

                primaryKeysInBaseTable.Add(exposedPrimaryKeyName!);
            }

            IEnumerable<string> missingKeys = primaryKeysInRequest.Except(baseTableDefinition.PrimaryKey);

            if (missingKeys.Any())
            {
                throw new DataApiBuilderException(
                    message: $"The request is invalid since the primary keys: " +
                             string.Join(", ", missingKeys) +
                             " requested were not found in the entity definition.",
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }
        }

        /// <summary>
        /// Validates a stored procedure request does not specify a primary key route.
        /// Applies to all stored procedure requests, both Queries and Mutations
        /// Mutations also validated using ValidateInsertRequestContext call in RestService
        /// </summary>
        /// <param name="primaryKeyRoute">Primary key route from the url.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void ValidateStoredProcedureRequest(string? primaryKeyRoute)
        {
            if (!string.IsNullOrWhiteSpace(primaryKeyRoute))
            {
                throw new DataApiBuilderException(
                    message: "Primary key route not supported for this entity.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates all required input parameters are supplied by request, and no extraneous parameters are provided
        /// Checks query string for Find operations, body for all other operations
        /// Defers type checking until parameterizing stage to prevent duplicating work
        /// </summary>
        public static void ValidateStoredProcedureRequestContext(
            StoredProcedureRequestContext spRequestCtx,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            StoredProcedureDefinition storedProcedureDefinition =
                TryGetStoredProcedureDefinition(spRequestCtx.EntityName, sqlMetadataProvider);

            HashSet<string> missingFields = new();
            HashSet<string> extraFields = new(spRequestCtx.ResolvedParameters.Keys);
            foreach ((string paramKey, ParameterDefinition paramDefinition) in storedProcedureDefinition.Parameters)
            {
                // If parameter not specified in request OR config
                if (!spRequestCtx.ResolvedParameters!.ContainsKey(paramKey)
                    && !paramDefinition.HasConfigDefault)
                {
                    // Ideally should check if a default is set in sql, but no easy way to do so - would have to parse procedure's object definition
                    // See https://docs.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-parameters-transact-sql?view=sql-server-ver16#:~:text=cursor%2Dreference%20parameter.-,has_default_value,-bit
                    // For SQL Server not populating this metadata for us; MySQL doesn't seem to allow parameter defaults so not relevant. 
                    missingFields.Add(paramKey);
                }
                else
                {
                    extraFields.Remove(paramKey);
                }
            }

            // If query string or body contains extra parameters that don't exist
            // TO DO: If the request header contains x-ms-must-match custom header with value of "ignore"
            // this should not throw any error. Tracked by issue #158.
            if (extraFields.Count > 0)
            {
                throw new DataApiBuilderException(
                    message: $"Invalid request. Contained unexpected fields: {string.Join(", ", extraFields)}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // If missing a parameter in the request and do not have a default specified in config
            if (missingFields.Count > 0)
            {
                throw new DataApiBuilderException(
                    message: $"Invalid request. Missing required procedure parameters: {string.Join(", ", missingFields)}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

        }

        /// <summary>
        /// Validates the primarykeyroute is populated with respect to an Update or Upsert operation.
        /// </summary>
        /// <param name="requestBody">Request body content</param>
        /// <exception cref="DataApiBuilderException">Thrown when request body is invalid JSON
        /// or JSON is a batch request (mutation of more than one entity in a single request).</exception>
        /// <returns>JsonElement representing the body of the request.</returns>
        public static JsonElement ParseRequestBody(string requestBody)
        {
            JsonElement mutationPayloadRoot = new();

            if (!string.IsNullOrEmpty(requestBody))
            {
                try
                {
                    using JsonDocument payload = JsonDocument.Parse(requestBody);
                    mutationPayloadRoot = payload.RootElement.Clone();
                }
                catch (JsonException)
                {
                    throw new DataApiBuilderException(
                        message: REQUEST_BODY_INVALID_JSON_ERR_MESSAGE,
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                        );
                }

                if (mutationPayloadRoot.ValueKind is JsonValueKind.Array)
                {
                    throw new DataApiBuilderException(
                        statusCode: HttpStatusCode.BadRequest,
                        message: BATCH_MUTATION_UNSUPPORTED_ERR_MESSAGE,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }

            return mutationPayloadRoot;
        }

        /// <summary>
        /// Validates the request body and queryString with respect to an Insert operation.
        /// </summary>
        /// <param name="queryString">queryString e.g. "$?filter="</param>
        /// <param name="requestBody">The string JSON body from the request.</param>
        /// <exception cref="DataApiBuilderException">Raised when queryString is present or invalid JSON in requestBody is found.</exception>
        public static JsonElement ValidateInsertRequest(string queryString, string requestBody)
        {
            if (!string.IsNullOrEmpty(queryString))
            {
                throw new DataApiBuilderException(
                    message: QUERY_STRING_INVALID_USAGE_ERR_MESSAGE,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            return ParseRequestBody(requestBody);
        }

        /// <summary>
        /// Validates the primarykeyroute is populated with respect to an Update or Upsert operation.
        /// </summary>
        /// <param name="primaryKeyRoute">URL route e.g. "Entity/id/1"</param>
        /// <param name="requestBody">The string JSON body from the request.</param>
        /// <exception cref="DataApiBuilderException">Raised when either primary key route is absent or invalid JSON in requestBody is found.</exception>
        public static JsonElement ValidateUpdateOrUpsertRequest(string? primaryKeyRoute, string requestBody)
        {
            if (string.IsNullOrWhiteSpace(primaryKeyRoute))
            {
                throw new DataApiBuilderException(
                    message: PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            return ParseRequestBody(requestBody);
        }

        /// <summary>
        /// Validates the primarykeyroute is populated with respect to a Delete operation.
        /// </summary>
        /// <param name="primaryKeyRoute">URL route e.g. "Entity/id/1"</param>
        /// <exception cref="DataApiBuilderException">Raised when primary key route is absent</exception>
        public static void ValidateDeleteRequest(string? primaryKeyRoute)
        {
            if (string.IsNullOrWhiteSpace(primaryKeyRoute))
            {
                throw new DataApiBuilderException(
                    message: PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the request body of an insert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="insertRequestCtx">Insert Request context containing the request body.</param>
        /// <param name="sqlMetadataProvider">To get the table definition.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public async Task<bool> ValidateInsertRequestContext(
            InsertRequestContext insertRequestCtx,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            IEnumerable<string> fieldsInRequestBody = insertRequestCtx.FieldValuePairsInBody.Keys;
            insertRequestCtx.BaseEntityName =
                await TryGetBaseEntityName(insertRequestCtx, sqlMetadataProvider);

            TableDefinition baseTableDefinition =
                TryGetTableDefinition(insertRequestCtx.BaseEntityName, sqlMetadataProvider);

            // Each field that is checked against the DB schema is removed
            // from the hash set of unvalidated fields.
            // At the end, if we end up with extraneous unvalidated fields, we throw error.
            HashSet<string> unvalidatedFields = new(fieldsInRequestBody);

            foreach ((string colName, ColumnDefinition colDef) in baseTableDefinition.Columns)
            {
                string aliasName = insertRequestCtx.ColumnAliases.ContainsKey(colName) ?
                    insertRequestCtx.ColumnAliases[colName] : colName;

                // if column is not exposed we skip
                if (!sqlMetadataProvider.TryGetExposedColumnName(
                    entityName: insertRequestCtx.EntityName,
                    backingFieldName: aliasName,
                    out string? exposedName))
                {
                    continue;
                }

                // Request body must have value defined for included non-nullable columns
                if (!colDef.IsNullable && fieldsInRequestBody.Contains(exposedName))
                {
                    if (insertRequestCtx.FieldValuePairsInBody[exposedName!] is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"Invalid value for field {exposedName} in request body.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                }

                // The insert operation behaves like a replacement update, since it
                // requires nullable fields to be defined in the request.
                if (ValidateColumn(colDef,
                                   exposedName!,
                                   fieldsInRequestBody,
                                   isReplacementUpdate: true))
                {
                    unvalidatedFields.Remove(exposedName!);
                }
            }
            // TO DO: If the request header contains x-ms-must-match custom header with value of "ignore"
            // this should not throw any error. Tracked by issue #158.
            if (unvalidatedFields.Any())
            {
                throw new DataApiBuilderException(
                    message: $"Invalid request body. Contained unexpected fields in body: {string.Join(", ", unvalidatedFields)}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            return true;
        }

        private async Task<string> TryGetBaseEntityName(RestRequestContext requestCtx, ISqlMetadataProvider sqlMetadataProvider)
        {
            if (_queryExecutor.GetType() != typeof(MsSqlQueryExecutor)
                || requestCtx.DatabaseObject.ObjectType is not SourceType.View)
            {
                return requestCtx.EntityName;
            }

            // This logic is specific to MsSql views.

            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();
            string entitySourceName = runtimeConfig.Entities[requestCtx.EntityName].GetSourceName();
            string query = "SELECT name as col_name, source_table, source_column, source_schema, is_hidden " +
                           "FROM sys.dm_exec_describe_first_result_set (N'SELECT * from " +
                           $"{entitySourceName}', null, 1)";
            JsonArray? resultArray = await _queryExecutor.ExecuteQueryAsync(
                sqltext: query,
                parameters: null,
                dataReaderHandler: _queryExecutor.GetJsonArrayAsync);
            JsonDocument sqlResult = JsonDocument.Parse(resultArray!.ToJsonString());
            Dictionary<string, string> colToBaseColMapping = new();
            Dictionary<string, string> colToBaseTableMapping = new();
            Dictionary<string, string> baseColToColMapping = new();
            string sourceTableForEntity = string.Empty;
            string sourceSchemaForEntity = string.Empty;
            foreach (JsonElement element in sqlResult.RootElement.EnumerateArray())
            {
                string colName = element.GetProperty("col_name").ToString();
                string sourceTable = element.GetProperty("source_table").ToString();
                string sourceColumn = element.GetProperty("source_column").ToString();
                string sourceSchema = element.GetProperty("source_schema").ToString();
                bool isHidden = Boolean.Parse(element.GetProperty("is_hidden").ToString());

                if (isHidden)
                {
                    // If the column is hidden, it is not included in the select
                    // statement of the view.
                    continue;
                }

                if (requestCtx.FieldValuePairsInBody.Keys.Contains(colName))
                {
                    if (string.Empty.Equals(sourceTableForEntity))
                    {
                        sourceTableForEntity = sourceTable;
                        sourceSchemaForEntity = sourceSchema;
                    }
                    else if (!sourceTableForEntity.Equals(sourceTable) ||
                        !sourceSchemaForEntity.Equals(sourceSchema))
                    {
                        // Mutation operation on entity based on multiple base tables
                        // is not allowed.
                        throw new DataApiBuilderException(
                            message: "Not all the fields in the request body belong to the same base table",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                            );
                    }
                }

                colToBaseColMapping.Add(colName, sourceColumn);
                colToBaseTableMapping.Add(colName, sourceTable);
            }

            foreach ((string colName, string sourceTable) in colToBaseTableMapping)
            {
                if (sourceTable.Equals(sourceTableForEntity))
                {
                    baseColToColMapping.Add(colToBaseColMapping[colName], colName);
                }
            }

            string fullSourceTableNameinDb = sourceSchemaForEntity + "." + sourceTableForEntity;
            string fullSourceTableNameinConfig = runtimeConfig.Entities[requestCtx.EntityName].GetSourceName();

            if (fullSourceTableNameinConfig.StartsWith(".") && !fullSourceTableNameinConfig.StartsWith("dbo."))
            {
                fullSourceTableNameinConfig = "dbo." + fullSourceTableNameinConfig;
            }

            requestCtx.ColumnAliases = baseColToColMapping;
            if (!string.Empty.Equals(sourceTableForEntity))
            {
                return sqlMetadataProvider.GetEntityNameFromSource(sourceTableForEntity);
            }

            return requestCtx.EntityName;
        }

        /// <summary>
        /// Validates the request body of an Upsert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="upsertRequestCtx">Upsert Request context containing the request body.</param>
        /// <param name="sqlMetadataProvider">To get the table definition.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public async Task<bool> ValidateUpsertRequestContext(
            UpsertRequestContext upsertRequestCtx,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            IEnumerable<string> fieldsInRequestBody = upsertRequestCtx.FieldValuePairsInBody.Keys;

            upsertRequestCtx.BaseEntityName =
                await TryGetBaseEntityName(upsertRequestCtx, sqlMetadataProvider);

            TableDefinition baseTableDefinition =
                TryGetTableDefinition(upsertRequestCtx.BaseEntityName, sqlMetadataProvider);

            // Each field that is checked against the DB schema is removed
            // from the hash set of unvalidated fields.
            // At the end, if we end up with extraneous unvalidated fields, we throw error.
            HashSet<string> unValidatedFields = new(fieldsInRequestBody);

            foreach ((string colName, ColumnDefinition colDef) in baseTableDefinition.Columns)
            {
                string aliasName = upsertRequestCtx.ColumnAliases.ContainsKey(colName) ?
                    upsertRequestCtx.ColumnAliases[colName] : colName;

                // if column is not exposed we skip
                if (!sqlMetadataProvider.TryGetExposedColumnName(
                    entityName: upsertRequestCtx.EntityName,
                    backingFieldName: aliasName,
                    out string? exposedName))
                {
                    continue;
                }

                // Primary Key(s) should not be present in the request body. We do not fail a request
                // if a PK is autogenerated here, because an UPSERT request may only need to update a
                // record. If an insert occurs on a table with autogenerated primary key,
                // a database error will be returned.
                if (baseTableDefinition.PrimaryKey.Contains(colName))
                {
                    continue;
                }

                // Request body must have value defined for included non-nullable columns
                if (!colDef.IsNullable && fieldsInRequestBody.Contains(exposedName))
                {
                    if (upsertRequestCtx.FieldValuePairsInBody[exposedName!] is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"Invalid value for field {exposedName} in request body.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                }

                bool isReplacementUpdate = (upsertRequestCtx.OperationType == Operation.Upsert) ? true : false;
                if (ValidateColumn(colDef, exposedName!, fieldsInRequestBody, isReplacementUpdate))
                {
                    unValidatedFields.Remove(exposedName!);
                }
            }

            // TO DO: If the request header contains x-ms-must-match custom header with value of "ignore"
            // this should not throw any error. Tracked by issue #158.
            if (unValidatedFields.Any())
            {
                throw new DataApiBuilderException(
                    message: "Invalid request body. Either insufficient or extra fields supplied.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            return true;
        }

        /// <summary>
        /// Validates a given column by checking that the column's properties
        /// are valid for the provided request body.
        /// </summary>
        /// <param name="column">The column to be verified.</param>
        /// <param name="fieldsInRequestBody">The fields in the request body.</param>
        /// <returns>true if the column is validated.</returns>
        private static bool ValidateColumn(ColumnDefinition column,
                                           string exposedName,
                                           IEnumerable<string> fieldsInRequestBody,
                                           bool isReplacementUpdate)
        {
            string message;
            // Autogenerated values should never be present in a request body.
            if (column.IsAutoGenerated && fieldsInRequestBody.Contains(exposedName))
            {
                message = $"Invalid request body. Field not allowed in body: {exposedName}.";
            }
            // Non-nullable fields must be in the body unless the request is not a replacement update.
            else if (!column.IsAutoGenerated && !column.IsNullable &&
                     !column.HasDefault && !fieldsInRequestBody.Contains(exposedName) &&
                     isReplacementUpdate)
            {
                message = $"Invalid request body. Missing field in body: {exposedName}.";
            }
            else
            {
                return true;
            }

            throw new DataApiBuilderException(
                message: message,
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
        }

        /// <summary>
        /// Validates that the entity in the request is valid.
        /// </summary>
        /// <param name="entityName">entity in the request.</param>
        /// <param name="entities">collection of valid entities.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void ValidateEntity(string entityName, IEnumerable<string> entities)
        {
            if (!entities.Contains(entityName))
            {
                throw new DataApiBuilderException(
                    message: $"{entityName} is not a valid entity.",
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }
        }

        /// <summary>
        /// Tries to get the table definition for the given entity from the Metadata provider.
        /// </summary>
        /// <param name="entityName">Target entity name.</param>
        /// <param name="sqlMetadataProvider">SqlMetadata provider that
        /// enables referencing DB schema.</param>
        /// <exception cref="DataApiBuilderException"></exception>

        private static TableDefinition TryGetTableDefinition(string entityName, ISqlMetadataProvider sqlMetadataProvider)
        {
            try
            {
                TableDefinition tableDefinition = sqlMetadataProvider.GetTableDefinition(entityName);
                return tableDefinition;
            }
            catch (KeyNotFoundException)
            {
                throw new DataApiBuilderException(
                    message: $"TableDefinition for entity: {entityName} does not exist.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Tries to get the stored procedure definition for the given entity
        /// Throws a DataApiBuilderException to return Bad Request to client instead of Unexpected Error
        /// Useful for accessing the definition within the request pipeline
        /// </summary>
        private static StoredProcedureDefinition TryGetStoredProcedureDefinition(string entityName, ISqlMetadataProvider sqlMetadataProvider)
        {
            try
            {
                return sqlMetadataProvider.GetStoredProcedureDefinition(entityName);
            }
            catch (InvalidCastException)
            {
                throw new DataApiBuilderException(
                    message: $"Underlying database object for entity {entityName} does not exist.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Helper function checks the $first query param
        /// to be sure that it can parse to a uint > 0
        /// </summary>
        /// <param name="first">String representing value associated with $first</param>
        /// <returns>uint > 0 representing $first</returns>
        public static uint CheckFirstValidity(string first)
        {
            if (!uint.TryParse(first, out uint firstAsUint) || firstAsUint == 0)
            {
                throw new DataApiBuilderException(
                        message: $"Invalid number of items requested, $first must be an integer greater than 0. Actual value: {first}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            return firstAsUint;
        }
    }
}

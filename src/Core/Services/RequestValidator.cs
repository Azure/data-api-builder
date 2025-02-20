// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Class which validates input supplied by a REST request.
    /// </summary>
    public class RequestValidator
    {
        public const string REQUEST_BODY_INVALID_JSON_ERR_MESSAGE = "Request body contains invalid JSON.";
        public const string BATCH_MUTATION_UNSUPPORTED_ERR_MESSAGE = "A Mutation operation on more than one entity in a single request is not yet supported.";
        public const string QUERY_STRING_INVALID_USAGE_ERR_MESSAGE = "Query string for this HTTP request type is an invalid URL.";
        public const string PRIMARY_KEY_INVALID_USAGE_ERR_MESSAGE = "Primary key for POST requests can't be specified in the request URL. Use request body instead.";
        public const string PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE = "Primary Key for this HTTP request type is required.";

        private readonly IMetadataProviderFactory _sqlMetadataProviderFactory;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public RequestValidator(IMetadataProviderFactory sqlMetadataProviderFactory, RuntimeConfigProvider runtimeConfigProvider)
        {
            _sqlMetadataProviderFactory = sqlMetadataProviderFactory;
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Validates the given request by ensuring:
        /// - each field to be returned is one of the exposed names for the entity.
        /// - extra fields specified in the body, will be discarded.
        /// </summary>
        /// <param name="context">Request context containing the REST operation fields and their values.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void ValidateRequestContext(RestRequestContext context)
        {
            ISqlMetadataProvider sqlMetadataProvider = GetSqlMetadataProvider(context.EntityName);
            SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(context.EntityName);
            foreach (string field in context.FieldsToBeReturned)
            {
                // Get backing column and check that column is valid
                if (!sqlMetadataProvider.TryGetBackingColumn(context.EntityName, field, out string? backingColumn) ||
                    !sourceDefinition.Columns.ContainsKey(backingColumn!))
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
        /// <exception cref="DataApiBuilderException"></exception>
        public void ValidatePrimaryKey(RestRequestContext context)
        {
            ISqlMetadataProvider sqlMetadataProvider = GetSqlMetadataProvider(context.EntityName);
            SourceDefinition sourceDefinition = TryGetSourceDefinition(context.EntityName, sqlMetadataProvider);

            int countOfPrimaryKeysInSchema = sourceDefinition.PrimaryKey.Count;
            int countOfPrimaryKeysInRequest = context.PrimaryKeyValuePairs.Count;

            if (countOfPrimaryKeysInRequest != countOfPrimaryKeysInSchema)
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
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.InvalidIdentifierField);
                }

                primaryKeysInRequest.Add(backingColumn!);
            }

            IEnumerable<string> missingKeys = primaryKeysInRequest.Except(sourceDefinition.PrimaryKey);

            // Verify each primary key is present in the object (table/view) definition.
            if (missingKeys.Any())
            {
                throw new DataApiBuilderException(
                    message: $"The request is invalid since the primary keys: " +
                             string.Join(", ", missingKeys) +
                             " requested were not found in the entity definition.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.InvalidIdentifierField);
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
        public void ValidateStoredProcedureRequestContext(StoredProcedureRequestContext spRequestCtx)
        {
            ISqlMetadataProvider sqlMetadataProvider = GetSqlMetadataProvider(spRequestCtx.EntityName);
            StoredProcedureDefinition storedProcedureDefinition =
                TryGetStoredProcedureDefinition(spRequestCtx.EntityName, sqlMetadataProvider);
            HashSet<string> extraFields = new(spRequestCtx.ResolvedParameters.Keys);
            foreach ((string paramKey, ParameterDefinition paramDefinition) in storedProcedureDefinition.Parameters)
            {
                // If a required stored procedure parameter value is missing in the request and
                // the runtime config doesn't define default value, the request is invalid.
                // Ideally should check if a default is set in sql, but no easy way to do so - would have to parse procedure's object definition
                // See https://docs.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-parameters-transact-sql?view=sql-server-ver16#:~:text=cursor%2Dreference%20parameter.-,has_default_value,-bit
                // For SQL Server not populating this metadata for us; MySQL doesn't seem to allow parameter defaults so not relevant. 
                if (spRequestCtx.ResolvedParameters!.ContainsKey(paramKey)
                    || paramDefinition.HasConfigDefault)
                {
                    extraFields.Remove(paramKey);
                }
            }

            // If query string or body contains extra parameters that don't exist
            // TO DO: If the request header contains x-ms-must-match custom header with value of "ignore"
            // this should not throw any error. Tracked by issue #158.
            if (extraFields.Count > 0 && IsRequestBodyStrict())
            {
                throw new DataApiBuilderException(
                    message: $"Invalid request. Contained unexpected fields: {string.Join(", ", extraFields)}" +
                                $" for entity: {spRequestCtx.EntityName}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the request body is a valid JSON string and is not a JSON array since we only
        /// support point operations.
        /// </summary>
        /// <param name="requestBody">Request body content</param>
        /// <exception cref="DataApiBuilderException">Thrown when request body is invalid JSON
        /// or JSON is a batch request (mutation of more than one entity in a single request).</exception>
        /// <returns>JsonElement representing the body of the request.</returns>
        public static JsonElement ValidateAndParseRequestBody(string requestBody)
        {
            JsonElement mutationPayloadRoot = new();

            if (!string.IsNullOrEmpty(requestBody))
            {
                try
                {
                    using JsonDocument payload = JsonDocument.Parse(requestBody);
                    mutationPayloadRoot = payload.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new DataApiBuilderException(
                        message: REQUEST_BODY_INVALID_JSON_ERR_MESSAGE,
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                        innerException: ex
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
        /// Performs validations on primary key route and queryString specified in the request URL, whose specifics depend on the type of operation being executed.
        /// Eg. a POST request cannot have primary key route/query string in the URL while it is mandatory for a PUT/PATCH/DELETE request to have a primary key route in the URL.
        /// </summary>
        /// <param name="operationType">Type of operation being executed.</param>
        /// <param name="primaryKeyRoute">URL route e.g. "Entity/id/1"</param>
        /// <param name="queryString">queryString e.g. "$?filter="</param>
        /// <exception cref="DataApiBuilderException">Raised when primaryKeyRoute/queryString fail the validations for the operation.</exception>
        public static void ValidatePrimaryKeyRouteAndQueryStringInURL(EntityActionOperation operationType, string? primaryKeyRoute = null, string? queryString = null)
        {
            bool isPrimaryKeyRouteEmpty = string.IsNullOrEmpty(primaryKeyRoute);
            bool isQueryStringEmpty = string.IsNullOrEmpty(queryString);

            switch (operationType)
            {
                case EntityActionOperation.Insert:
                    if (!isPrimaryKeyRouteEmpty)
                    {
                        throw new DataApiBuilderException(
                            message: PRIMARY_KEY_INVALID_USAGE_ERR_MESSAGE,
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    if (!isQueryStringEmpty)
                    {
                        throw new DataApiBuilderException(
                            message: QUERY_STRING_INVALID_USAGE_ERR_MESSAGE,
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    break;
                case EntityActionOperation.Delete:
                case EntityActionOperation.Update:
                case EntityActionOperation.UpdateIncremental:
                case EntityActionOperation.Upsert:
                case EntityActionOperation.UpsertIncremental:
                    /// Validate that the primarykeyroute is populated for these operations.
                    if (isPrimaryKeyRouteEmpty)
                    {
                        throw new DataApiBuilderException(
                            message: PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE,
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    break;
                default:
                    throw new DataApiBuilderException(
                        message: "Unexpected operation encountered. Cannot perform validations on URL components.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }
        }

        /// <summary>
        /// Validates the request body of an insert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="insertRequestCtx">Insert Request context containing the request body.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void ValidateInsertRequestContext(InsertRequestContext insertRequestCtx)
        {
            ISqlMetadataProvider sqlMetadataProvider = GetSqlMetadataProvider(insertRequestCtx.EntityName);

            IEnumerable<string> fieldsInRequestBody = insertRequestCtx.FieldValuePairsInBody.Keys;
            SourceDefinition sourceDefinition =
                TryGetSourceDefinition(insertRequestCtx.EntityName, sqlMetadataProvider);

            bool isRequestBodyStrict = IsRequestBodyStrict();

            // Each field that is checked against the DB schema is removed
            // from the hash set of unvalidated fields.
            // At the end, if we end up with extraneous unvalidated fields, we throw error.
            HashSet<string> unvalidatedFields = new(fieldsInRequestBody);
            foreach (KeyValuePair<string, ColumnDefinition> column in sourceDefinition.Columns)
            {
                // if column is not exposed we skip
                if (!sqlMetadataProvider.TryGetExposedColumnName(
                    entityName: insertRequestCtx.EntityName,
                    backingFieldName: column.Key,
                    out string? exposedName))
                {
                    continue;
                }

                if (insertRequestCtx.FieldValuePairsInBody.ContainsKey(exposedName!) && column.Value.IsReadOnly)
                {
                    if (isRequestBodyStrict)
                    {
                        throw new DataApiBuilderException(
                            message: $"Field '{exposedName}' cannot be included in the request body.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    unvalidatedFields.Remove(exposedName!);
                }

                // Request body must have value defined for included non-nullable columns
                if (!column.Value.IsNullable && fieldsInRequestBody.Contains(exposedName))
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
                if (ValidateColumn(column.Value,
                                   exposedName!,
                                   fieldsInRequestBody,
                                   isReplacementUpdate: true,
                                   isRequestBodyStrict))
                {
                    unvalidatedFields.Remove(exposedName!);
                }
            }

            // There may be unvalidated fields remaining because of extraneous fields in request body
            // which are not mapped to the table. We throw an exception only when we operate in strict mode,
            // i.e. when extraneous fields are not allowed.
            if (unvalidatedFields.Any() && isRequestBodyStrict)
            {
                throw new DataApiBuilderException(
                    message: $"Invalid request body. Contained unexpected fields in body: {string.Join(", ", unvalidatedFields)}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the request body of an Upsert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="upsertRequestCtx">Upsert Request context containing the request body.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void ValidateUpsertRequestContext(UpsertRequestContext upsertRequestCtx)
        {
            ISqlMetadataProvider sqlMetadataProvider = GetSqlMetadataProvider(upsertRequestCtx.EntityName);
            IEnumerable<string> fieldsInRequestBody = upsertRequestCtx.FieldValuePairsInBody.Keys;
            bool isRequestBodyStrict = IsRequestBodyStrict();
            SourceDefinition sourceDefinition = TryGetSourceDefinition(upsertRequestCtx.EntityName, sqlMetadataProvider);

            // Each field that is checked against the DB schema is removed
            // from the hash set of unvalidated fields.
            // At the end, if we end up with extraneous unvalidated fields, we throw error.
            HashSet<string> unValidatedFields = new(fieldsInRequestBody);

            foreach (KeyValuePair<string, ColumnDefinition> column in sourceDefinition.Columns)
            {
                // if column is not exposed we skip
                if (!sqlMetadataProvider.TryGetExposedColumnName(
                    entityName: upsertRequestCtx.EntityName,
                    backingFieldName: column.Key,
                    out string? exposedName))
                {
                    continue;
                }

                if (upsertRequestCtx.FieldValuePairsInBody.ContainsKey(exposedName!) && column.Value.IsReadOnly)
                {
                    if (isRequestBodyStrict)
                    {
                        throw new DataApiBuilderException(
                            message: $"Field '{exposedName}' cannot be included in the request body.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    unValidatedFields.Remove(exposedName!);
                }

                // Primary Key(s) should not be present in the request body. We do not fail a request
                // if a PK is autogenerated here, because an UPSERT request may only need to update a
                // record. If an insert occurs on a table with autogenerated primary key,
                // a database error will be returned.
                if (sourceDefinition.PrimaryKey.Contains(column.Key))
                {
                    continue;
                }

                // Request body must have value defined for included non-nullable columns
                if (!column.Value.IsNullable && fieldsInRequestBody.Contains(exposedName))
                {
                    if (upsertRequestCtx.FieldValuePairsInBody[exposedName!] is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"Invalid value for field {exposedName} in request body.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                }

                bool isReplacementUpdate = (upsertRequestCtx.OperationType == EntityActionOperation.Upsert) ? true : false;
                if (ValidateColumn(column.Value, exposedName!, fieldsInRequestBody, isReplacementUpdate, isRequestBodyStrict))
                {
                    unValidatedFields.Remove(exposedName!);
                }
            }

            // There may be unvalidated fields remaining because of extraneous fields in request body
            // which are not mapped to the table. We throw an exception only when we operate in strict mode,
            // i.e. when extraneous fields are not allowed.
            if (unValidatedFields.Any() && isRequestBodyStrict)
            {
                throw new DataApiBuilderException(
                    message: "Invalid request body. Either insufficient or extra fields supplied.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates a given column by checking that the column's properties
        /// are valid for the provided request body.
        /// </summary>
        /// <param name="column">The column to be verified.</param>
        /// <param name="exposedName">Exposed name of the column.</param>
        /// <param name="fieldsInRequestBody">The fields in the request body.</param>
        /// <param name="isReplacementUpdate">Indicates if the column is a replacement update.</param>
        /// <param name="isRequestBodyStrict">Indicates if the runtime setting is request body strict.</param>
        /// <returns>true if the column is validated.</returns>
        private static bool ValidateColumn(ColumnDefinition column,
                                           string exposedName,
                                           IEnumerable<string> fieldsInRequestBody,
                                           bool isReplacementUpdate,
                                           bool isRequestBodyStrict)
        {
            string message;
            // Read-only fields should never be present in a request body, unless request-body-strict is false.
            if (column.IsReadOnly && fieldsInRequestBody.Contains(exposedName) && isRequestBodyStrict)
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
        /// Validates that the request denoted entity is defined in the runtime configuration.
        /// </summary>
        /// <param name="entityName">entity in the request.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public void ValidateEntity(string entityName)
        {
            ISqlMetadataProvider sqlMetadataProvider = GetSqlMetadataProvider(entityName);
            IEnumerable<string> entities = sqlMetadataProvider.EntityToDatabaseObject.Keys;
            if (!entities.Contains(entityName) || sqlMetadataProvider.GetLinkingEntities().ContainsKey(entityName))
            {
                // Do not validate the entity if the entity definition does not exist or if the entity is a linking entity.
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
        /// enables referencing DB schema.</param>
        /// <exception cref="DataApiBuilderException"></exception>

        private static SourceDefinition TryGetSourceDefinition(string entityName, ISqlMetadataProvider sqlMetadataProvider)
        {
            try
            {
                SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
                return sourceDefinition;
            }
            catch (KeyNotFoundException ex)
            {
                throw new DataApiBuilderException(
                    message: $"Source definition for entity: {entityName} does not exist.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: ex);
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
            catch (InvalidCastException ex)
            {
                throw new DataApiBuilderException(
                    message: $"Underlying database object for entity {entityName} does not exist.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: ex);
            }
        }

        /// <summary>
        /// Helper method checks the $first query param
        /// to be sure that it can parse to a int > 0 or -1.
        /// </summary>
        /// <param name="first">String representing value associated with $first</param>
        /// <returns>int > 0 or int == -1 representing $first</returns>
        public static int CheckFirstValidity(string first)
        {
            if (!int.TryParse(first, out int firstAsInt) || firstAsInt == 0 || firstAsInt < -1)
            {
                throw new DataApiBuilderException(
                        message: $"Invalid number of items requested, $first must be -1 or an integer greater than 0. Actual value: {first}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            return firstAsInt;
        }

        /// <summary>
        /// Helper method to check if the request body for REST allows extra fields.
        /// </summary>
        /// <returns>true if extra fields are not allowed in REST request body.</returns>
        private bool IsRequestBodyStrict()
        {
            if (_runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                return runtimeConfig.IsRequestBodyStrict;
            }

            return true;
        }

        /// <summary>
        /// Helper method to get the sqlMetadataProvider from the entity name.
        /// </summary>
        private ISqlMetadataProvider GetSqlMetadataProvider(string entityName)
        {
            string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
            return _sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
        }
    }
}

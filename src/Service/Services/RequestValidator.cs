using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Class which validates input supplied by a REST request.
    /// </summary>
    public class RequestValidator
    {
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
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, sqlMetadataProvider);

            int countOfPrimaryKeysInSchema = tableDefinition.PrimaryKey.Count;
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
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
                }

                primaryKeysInRequest.Add(backingColumn!);
            }

            // Verify each primary key is present in the table definition.
            IEnumerable<string> missingKeys = primaryKeysInRequest.Except(tableDefinition.PrimaryKey);

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
        /// Validates the request body and queryString with respect to an Insert operation.
        /// </summary>
        /// <param name="queryString">Query string from the url.</param>
        /// <param name="requestBody">The string JSON body from the request.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static JsonElement ValidateInsertRequest(string queryString, string requestBody)
        {
            if (!string.IsNullOrEmpty(queryString))
            {
                throw new DataApiBuilderException(
                    message: "Query string for POST requests is an invalid url.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            JsonElement insertPayloadRoot = new();

            if (!string.IsNullOrEmpty(requestBody))
            {
                insertPayloadRoot = GetInsertPayload(requestBody);
            }

            return insertPayloadRoot;
        }

        /// <summary>
        /// Validates the primarykeyroute is populated with respect to a Delete operation.
        /// </summary>
        /// <param name="primaryKeyRoute">Primary key route from the url.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void ValidateDeleteRequest(string? primaryKeyRoute)
        {
            if (string.IsNullOrEmpty(primaryKeyRoute))
            {
                throw new DataApiBuilderException(
                    message: "Primary Key for DELETE requests is required.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates a stored procedure request does not specify a primary key route.
        /// Applies to all stored procedure requests, both Queries and Mutations
        /// Mutations also validated using ValidateInsertRequestContext call in RestService
        /// </summary>
        /// <param name="primaryKeyRoute">Primary key route from the url.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static void ValidateStoredProcedureRequest(string? primaryKeyRoute)
        {
            if (!string.IsNullOrWhiteSpace(primaryKeyRoute))
            {
                throw new DataGatewayException(
                    message: "Primary key route not supported for this entity.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
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
                throw new DataGatewayException(
                    message: $"Invalid request. Contained unexpected fields: {string.Join(", ", extraFields)}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }

            // If missing a parameter in the request and do not have a default specified in config
            if (missingFields.Count > 0)
            {
                throw new DataGatewayException(
                    message: $"Invalid request. Missing required procedure parameters: {string.Join(", ", missingFields)}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }

        }

        /// <summary>
        /// Validates the primarykeyroute is populated with respect to an Update or Upsert operation.
        /// </summary>
        /// <param name="primaryKeyRoute">Primary key route from the url.</param>
        /// <param name="requestBody">The body of the request.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        /// <returns>JsonElement representing the body of the request.</returns>
        public static JsonElement ValidateUpdateOrUpsertRequest(string? primaryKeyRoute, string requestBody)
        {
            if (string.IsNullOrEmpty(primaryKeyRoute))
            {
                throw new DataApiBuilderException(
                    message: "Primary Key for UPSERT requests is required.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            JsonElement upsertPayloadRoot = new();

            if (!string.IsNullOrEmpty(requestBody))
            {
                upsertPayloadRoot = GetInsertPayload(requestBody);
            }

            return upsertPayloadRoot;
        }

        /// <summary>
        /// Validates the request body of an insert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="insertRequestCtx">Insert Request context containing the request body.</param>
        /// <param name="sqlMetadataProvider">To get the table definition.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void ValidateInsertRequestContext(
            InsertRequestContext insertRequestCtx,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            IEnumerable<string> fieldsInRequestBody = insertRequestCtx.FieldValuePairsInBody.Keys;
            TableDefinition tableDefinition =
                TryGetTableDefinition(insertRequestCtx.EntityName, sqlMetadataProvider);

            // Each field that is checked against the DB schema is removed
            // from the hash set of unvalidated fields.
            // At the end, if we end up with extraneous unvalidated fields, we throw error.
            HashSet<string> unvalidatedFields = new(fieldsInRequestBody);

            foreach (KeyValuePair<string, ColumnDefinition> column in tableDefinition.Columns)
            {
                // if column is not exposed we skip
                if (!sqlMetadataProvider.TryGetExposedColumnName(
                    entityName: insertRequestCtx.EntityName,
                    backingFieldName: column.Key,
                    out string? exposedName))
                {
                    continue;
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
        }

        /// <summary>
        /// Validates the request body of an Upsert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="upsertRequestCtx">Upsert Request context containing the request body.</param>
        /// <param name="sqlMetadataProvider">To get the table definition.</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void ValidateUpsertRequestContext(
            UpsertRequestContext upsertRequestCtx,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            IEnumerable<string> fieldsInRequestBody = upsertRequestCtx.FieldValuePairsInBody.Keys;
            TableDefinition tableDefinition =
                TryGetTableDefinition(upsertRequestCtx.EntityName, sqlMetadataProvider);

            // Each field that is checked against the DB schema is removed
            // from the hash set of unvalidated fields.
            // At the end, if we end up with extraneous unvalidated fields, we throw error.
            HashSet<string> unValidatedFields = new(fieldsInRequestBody);

            foreach (KeyValuePair<string, ColumnDefinition> column in tableDefinition.Columns)
            {
                // if column is not exposed we skip
                if (!sqlMetadataProvider.TryGetExposedColumnName(
                    entityName: upsertRequestCtx.EntityName,
                    backingFieldName: column.Key,
                    out string? exposedName))
                {
                    continue;
                }

                // Primary Key(s) should not be present in the request body. We do not fail a request
                // if a PK is autogenerated here, because an UPSERT request may only need to update a
                // record. If an insert occurs on a table with autogenerated primary key,
                // a database error will be returned.
                if (tableDefinition.PrimaryKey.Contains(column.Key))
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

                bool isReplacementUpdate = (upsertRequestCtx.OperationType == Operation.Upsert) ? true : false;
                if (ValidateColumn(column.Value, exposedName!, fieldsInRequestBody, isReplacementUpdate))
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
        /// Throws a DataGatewayException to return Bad Request to client instead of Unexpected Error
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
                throw new DataGatewayException(
                    message: $"Underlying database object for entity {entityName} does not exist.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Creates a JSON payload from a string.
        /// </summary>
        /// <param name="requestBody">JSON string representation of request body</param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        private static JsonElement GetInsertPayload(string requestBody)
        {
            using JsonDocument insertPayload = JsonDocument.Parse(requestBody);

            if (insertPayload.RootElement.ValueKind == JsonValueKind.Array)
            {
                throw new DataApiBuilderException(
                    statusCode: HttpStatusCode.BadRequest,
                    message: "Mutation operation on many instances of an entity in a single request are not yet supported.",
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
            else
            {
                return insertPayload.RootElement.Clone();
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

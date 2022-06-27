using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Services
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
        /// <exception cref="DataGatewayException"></exception>
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
                    throw new DataGatewayException(
                        message: "Invalid field to be returned requested: " + field,
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                }
            }
        }

        /// <summary>
        /// Tries to validate the primary key in the request match those specified in the entity
        /// definition in the configuration file.
        /// </summary>
        /// <param name="context">Request context containing the primary keys and their values.</param>
        /// <param name="sqlMetadataProvider">To get the table definition.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static void ValidatePrimaryKey(
            RestRequestContext context,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, sqlMetadataProvider);

            int countOfPrimaryKeysInSchema = tableDefinition.PrimaryKey.Count;
            int countOfPrimaryKeysInRequest = context.PrimaryKeyValuePairs.Count;

            if (countOfPrimaryKeysInRequest != countOfPrimaryKeysInSchema)
            {
                throw new DataGatewayException(
                    message: "Primary key column(s) provided do not match DB schema.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }

            List<string> primaryKeysInRequest = new();
            foreach (string pk in context.PrimaryKeyValuePairs.Keys)
            {
                if (!sqlMetadataProvider.TryGetBackingColumn(context.EntityName, pk, out string? backingColumn))
                {
                    throw new DataGatewayException(
                    message: $"Primary key column: {pk} not found in the entity definition.",
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound);
                }

                primaryKeysInRequest.Add(backingColumn!);
            }

            // Verify each primary key is present in the table definition.
            IEnumerable<string> missingKeys = primaryKeysInRequest.Except(tableDefinition.PrimaryKey);

            if (missingKeys.Any())
            {
                throw new DataGatewayException(
                    message: $"The request is invalid since the primary keys: " +
                             string.Join(", ", missingKeys) +
                             " requested were not found in the entity definition.",
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound);
            }
        }

        /// <summary>
        /// Validates the request body and queryString with respect to an Insert operation.
        /// </summary>
        /// <param name="queryString">Query string from the url.</param>
        /// <param name="requestBody">The string JSON body from the request.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static JsonElement ValidateInsertRequest(string queryString, string requestBody)
        {
            if (!string.IsNullOrEmpty(queryString))
            {
                throw new DataGatewayException(
                    message: "Query string for POST requests is an invalid url.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
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
        /// <exception cref="DataGatewayException"></exception>
        public static void ValidateDeleteRequest(string? primaryKeyRoute)
        {
            if (string.IsNullOrEmpty(primaryKeyRoute))
            {
                throw new DataGatewayException(
                    message: "Primary Key for DELETE requests is required.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the primarykeyroute is populated with respect to an Update or Upsert operation.
        /// </summary>
        /// <param name="primaryKeyRoute">Primary key route from the url.</param>
        /// <param name="requestBody">The body of the request.</param>
        /// <exception cref="DataGatewayException"></exception>
        /// <returns>JsonElement representing the body of the request.</returns>
        public static JsonElement ValidateUpdateOrUpsertRequest(string? primaryKeyRoute, string requestBody)
        {
            if (string.IsNullOrEmpty(primaryKeyRoute))
            {
                throw new DataGatewayException(
                    message: "Primary Key for UPSERT requests is required.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
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
        /// <exception cref="DataGatewayException"></exception>
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
                // Request body must have value defined for included non-nullable columns
                if (!column.Value.IsNullable && fieldsInRequestBody.Contains(column.Key))
                {
                    if (insertRequestCtx.FieldValuePairsInBody[column.Key] == null)
                    {
                        throw new DataGatewayException(
                        message: $"Invalid value for field {column.Key} in request body.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                    }
                }

                // The insert operation behaves like a replacement update, since it
                // requires nullable fields to be defined in the request.
                if (ValidateColumn(column, fieldsInRequestBody, isReplacementUpdate: true))
                {
                    unvalidatedFields.Remove(column.Key);
                }
            }
            // TO DO: If the request header contains x-ms-must-match custom header with value of "ignore"
            // this should not throw any error. Tracked by issue #158.
            if (unvalidatedFields.Any())
            {
                throw new DataGatewayException(
                    message: $"Invalid request body. Contained unexpected fields in body: {string.Join(", ", unvalidatedFields)}",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the request body of an Upsert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="upsertRequestCtx">Upsert Request context containing the request body.</param>
        /// <param name="sqlMetadataProvider">To get the table definition.</param>
        /// <exception cref="DataGatewayException"></exception>
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
                // Primary Key(s) should not be present in the request body. We do not fail a request
                // if a PK is autogenerated here, because an UPSERT request may only need to update a
                // record. If an insert occurs on a table with autogenerated primary key,
                // a database error will be returned.
                if (tableDefinition.PrimaryKey.Contains(column.Key))
                {
                    continue;
                }

                // Request body must have value defined for included non-nullable columns
                if (!column.Value.IsNullable && fieldsInRequestBody.Contains(column.Key))
                {
                    if (upsertRequestCtx.FieldValuePairsInBody[column.Key] == null)
                    {
                        throw new DataGatewayException(
                        message: $"Invalid value for field {column.Key} in request body.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                    }
                }

                bool isReplacementUpdate = (upsertRequestCtx.OperationType == Operation.Upsert) ? true : false;
                if (ValidateColumn(column, fieldsInRequestBody, isReplacementUpdate))
                {
                    unValidatedFields.Remove(column.Key);
                }
            }

            // TO DO: If the request header contains x-ms-must-match custom header with value of "ignore"
            // this should not throw any error. Tracked by issue #158.
            if (unValidatedFields.Any())
            {
                throw new DataGatewayException(
                    message: "Invalid request body. Either insufficient or extra fields supplied.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates a given column by checking that the column's properties
        /// are valid for the provided request body.
        /// </summary>
        /// <param name="column">The column to be verified.</param>
        /// <param name="fieldsInRequestBody">The fields in the request body.</param>
        /// <returns>true if the column is validated.</returns>
        private static bool ValidateColumn(KeyValuePair<string, ColumnDefinition> column, IEnumerable<string> fieldsInRequestBody, bool isReplacementUpdate)
        {
            string message;
            // Autogenerated values should never be present in a request body.
            if (column.Value.IsAutoGenerated && fieldsInRequestBody.Contains(column.Key))
            {
                message = $"Invalid request body. Field not allowed in body: {column.Key}.";
            }
            // Non-nullable fields must be in the body unless the request is not a replacement update.
            else if (!column.Value.IsAutoGenerated && !column.Value.IsNullable && !column.Value.HasDefault && !fieldsInRequestBody.Contains(column.Key) && isReplacementUpdate)
            {
                message = $"Invalid request body. Missing field in body: {column.Key}.";
            }
            else
            {
                return true;
            }

            throw new DataGatewayException(
                message: message,
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
        }

        /// <summary>
        /// Validates that the entity in the request is valid.
        /// </summary>
        /// <param name="entityName">entity in the request.</param>
        /// <param name="entities">collection of valid entities.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static void ValidateEntity(string entityName, IEnumerable<string> entities)
        {
            if (!entities.Contains(entityName))
            {
                throw new DataGatewayException(
                    message: $"{entityName} is not a valid entity.",
                    statusCode: HttpStatusCode.NotFound,
                    subStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound);
            }
        }

        /// <summary>
        /// Tries to get the table definition for the given entity from the Metadata provider.
        /// </summary>
        /// <param name="entityName">Target entity name.</param>
        /// <param name="sqlMetadataProvider">SqlMetadata provider that
        /// enables referencing DB schema.</param>
        /// <exception cref="DataGatewayException"></exception>

        private static TableDefinition TryGetTableDefinition(string entityName, ISqlMetadataProvider sqlMetadataProvider)
        {
            try
            {
                TableDefinition tableDefinition = sqlMetadataProvider.GetTableDefinition(entityName);
                return tableDefinition;
            }
            catch (KeyNotFoundException)
            {
                throw new DataGatewayException(
                    message: $"TableDefinition for entity: {entityName} does not exist.",
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
                throw new DataGatewayException(
                    statusCode: HttpStatusCode.BadRequest,
                    message: "Mutation operation on many instances of an entity in a single request are not yet supported.",
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
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
                throw new DataGatewayException(
                        message: $"Invalid number of items requested, $first must be an integer greater than 0. Actual value: {first}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }

            return firstAsUint;
        }
    }
}

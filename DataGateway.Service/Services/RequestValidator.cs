using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Class which validates input supplied by a REST request.
    /// </summary>
    public class RequestValidator
    {
        /// <summary>
        /// Validates the given request by ensuring:
        /// - each field to be returned is one of the columns in the table.
        /// - extra fields specified in the body, will be discarded.
        /// </summary>
        /// <param name="context">Request context containing the REST operation fields and their values.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static void ValidateRequestContext(RestRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, configurationProvider);

            foreach (string field in context.FieldsToBeReturned)
            {
                if (!tableDefinition.Columns.ContainsKey(field))
                {
                    throw new DataGatewayException(
                        message: "Invalid Column name requested: " + field,
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
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static void ValidatePrimaryKey(RestRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, configurationProvider);

            int countOfPrimaryKeysInSchema = tableDefinition.PrimaryKey.Count;
            int countOfPrimaryKeysInRequest = context.PrimaryKeyValuePairs.Count;

            if (countOfPrimaryKeysInRequest != countOfPrimaryKeysInSchema)
            {
                throw new DataGatewayException(
                    message: "Primary key column(s) provided do not match DB schema.",
                    statusCode: HttpStatusCode.BadRequest,
                    DataGatewayException.SubStatusCodes.BadRequest);
            }

            // Verify each primary key is present in the table definition.
            List<string> primaryKeysInRequest = new(context.PrimaryKeyValuePairs.Keys);
            IEnumerable<string> missingKeys = primaryKeysInRequest.Except(tableDefinition.PrimaryKey);

            if (missingKeys.Any())
            {
                throw new DataGatewayException(
                    message: $"The request is invalid since the primary keys: " +
                        string.Join(", ", missingKeys) +
                        " requested were not found in the entity definition.",
                        statusCode: HttpStatusCode.BadRequest,
                        DataGatewayException.SubStatusCodes.BadRequest);
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
        public static void ValidateDeleteRequest(string primaryKeyRoute)
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
        /// Validates the primarykeyroute is populated with respect to an Upsert operation.
        /// </summary>
        /// <param name="primaryKeyRoute">Primary key route from the url.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static JsonElement ValidateUpsertRequest(string primaryKeyRoute, string requestBody)
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
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static void ValidateInsertRequestContext(
        InsertRequestContext insertRequestCtx,
        IMetadataStoreProvider configurationProvider)
        {
            IEnumerable<string> fieldsInRequestBody = insertRequestCtx.FieldValuePairsInBody.Keys;
            TableDefinition tableDefinition =
                TryGetTableDefinition(insertRequestCtx.EntityName, configurationProvider);

            // Each field that is checked against the DB schema is removed
            // from the hash set of unvalidated fields.
            // At the end, if we end up with extraneous unvalidated fields, we throw error.
            HashSet<string> unvalidatedFields = new(fieldsInRequestBody);

            foreach (KeyValuePair<string, ColumnDefinition> column in tableDefinition.Columns)
            {
                if (// If the column value is not autogenerated but nullable,
                    // it doesn't matter if it is in the body or not
                    (!column.Value.IsAutoGenerated && column.Value.IsNullable)
                    || // If the column value is not autogenerated and not nullable,
                       // then the body should contain that field.
                    (!column.Value.IsAutoGenerated && !column.Value.IsNullable && fieldsInRequestBody.Contains(column.Key))
                    || // If column value is autogenerated then it must not be in the request body
                    (column.Value.IsAutoGenerated && !fieldsInRequestBody.Contains(column.Key))
                    )
                {
                    // Since the field is now validated, remove it from the unvalidated fields.
                    // containing a value for it in the request body is valid.
                    unvalidatedFields.Remove(column.Key);
                }
                else
                {
                    throw new DataGatewayException(
                        message: $"Invalid request body. Either insufficient or unnecessary values for fields supplied.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                }
            }

            // TO DO: If the request header contains x-ms-must-match custom header with value of "ignore"
            // this should not throw any error. Tracked by issue #158.
            if (unvalidatedFields.Any())
            {
                throw new DataGatewayException(
                    message: $"Invalid request body. Contained unexpected fields in body: {string.Join(", ", unvalidatedFields)}", statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the request body of an Upsert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="upsertRequestCtx">Upsert Request context containing the request body.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DataGatewayException"></exception>
        public static void ValidateUpsertRequestContext(
        UpsertRequestContext upsertRequestCtx,
        IMetadataStoreProvider configurationProvider)
        {
            IEnumerable<string> fieldsInRequestBody = upsertRequestCtx.FieldValuePairsInBody.Keys;
            TableDefinition tableDefinition =
                TryGetTableDefinition(upsertRequestCtx.EntityName, configurationProvider);

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

                if (// If the column value is not autogenerated, then the body should contain that field.
                    (!column.Value.IsAutoGenerated && fieldsInRequestBody.Contains(column.Key))
                    || // If the column value is not nullable, then the body should contain that field.
                    (!column.Value.IsNullable && fieldsInRequestBody.Contains(column.Key))
                    || // If the column value is autogenerated, then the body should NOT contain that field.
                    (column.Value.IsAutoGenerated && !fieldsInRequestBody.Contains(column.Key)))
                {
                    // Since the field is now validated, remove it from the unvalidated fields.
                    // containing a value for it in the request body is valid.
                    unValidatedFields.Remove(column.Key);
                }
                else
                {
                    throw new DataGatewayException(
                        message: "Invalid request body. Either insufficient or unnecessary values for fields supplied.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
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
        /// Tries to get the table definition for the given entity from the configuration provider.
        /// </summary>
        /// <param name="entityName">Target entity name.</param>
        /// <param name="configurationProvider">Configuration provider that
        /// enables referencing DB schema in config.</param>
        /// <exception cref="DataGatewayException"></exception>

        private static TableDefinition TryGetTableDefinition(string entityName, IMetadataStoreProvider configurationProvider)
        {
            try
            {
                TableDefinition tableDefinition = configurationProvider.GetTableDefinition(entityName);
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
    }
}

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
        /// <exception cref="DatagatewayException"></exception>
        public static void ValidateRequestContext(RestRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, configurationProvider);

            foreach (string field in context.FieldsToBeReturned)
            {
                if (!tableDefinition.Columns.ContainsKey(field))
                {
                    throw new DatagatewayException(
                        message: "Invalid Column name requested: " + field,
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
                }
            }
        }

        /// <summary>
        /// Tries to validate the primary key in the request match those specified in the entity
        /// definition in the configuration file.
        /// </summary>
        /// <param name="context">Request context containing the primary keys and their values.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static void ValidatePrimaryKey(RestRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, configurationProvider);

            int countOfPrimaryKeysInSchema = tableDefinition.PrimaryKey.Count;
            int countOfPrimaryKeysInRequest = context.PrimaryKeyValuePairs.Count;

            if (countOfPrimaryKeysInRequest != countOfPrimaryKeysInSchema)
            {
                throw new DatagatewayException(
                    message: "Primary key column(s) provided do not match DB schema.",
                    statusCode: HttpStatusCode.BadRequest,
                    DatagatewayException.SubStatusCodes.BadRequest);
            }

            // Verify each primary key is present in the table definition.
            List<string> primaryKeysInRequest = new(context.PrimaryKeyValuePairs.Keys);
            IEnumerable<string> missingKeys = primaryKeysInRequest.Except(tableDefinition.PrimaryKey);

            if (missingKeys.Any())
            {
                throw new DatagatewayException(
                    message: $"The request is invalid since the primary keys: " +
                        string.Join(", ", missingKeys) +
                        " requested were not found in the entity definition.",
                        statusCode: HttpStatusCode.BadRequest,
                        DatagatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the request body and queryString with respect to an Insert operation.
        /// </summary>
        /// <param name="queryString">Query string from the url.</param>
        /// <param name="requestBody">The string JSON body from the request.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static JsonElement ValidateInsertRequest(string queryString, string requestBody)
        {
            if (!string.IsNullOrEmpty(queryString))
            {
                throw new DatagatewayException(
                    message: "Query string for POST requests is an invalid url.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
            }

            JsonElement insertPayloadRoot = new();

            if (!string.IsNullOrEmpty(requestBody))
            {
                using JsonDocument insertPayload = JsonDocument.Parse(requestBody);

                if (insertPayload.RootElement.ValueKind == JsonValueKind.Array)
                {
                    throw new NotSupportedException("InsertMany operations are not yet supported.");
                }
                else
                {
                    insertPayloadRoot = insertPayload.RootElement.Clone();
                }
            }

            return insertPayloadRoot;
        }

        /// <summary>
        /// Validates the primarykeyroute is populated with respect to a Delete operation.
        /// </summary>
        /// <param name="primaryKeyRoute">Primary key route from the url.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static void ValidateDeleteRequest(string primaryKeyRoute)
        {
            if (string.IsNullOrEmpty(primaryKeyRoute))
            {
                throw new DatagatewayException(
                    message: "Primary Key for DELETE requests is required.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the request body of an insert request context.
        /// Checks if all the fields necessary are present in the body, if they are not autogenerated
        /// and vice versa.
        /// </summary>
        /// <param name="insertRequestCtx">Insert Request context containing the request body.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>
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
                if (!column.Value.IsAutoGenerated && !fieldsInRequestBody.Contains(column.Key))
                {
                    throw new DatagatewayException(
                        message: $"Invalid request body. Missing required field in body: {column.Key}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
                }
                else if (column.Value.IsAutoGenerated && fieldsInRequestBody.Contains(column.Key))
                {
                    throw new DatagatewayException(
                        message: $"Invalid request body. Contained unexpected field in body: {column.Key}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
                }
                // Since the field is now validated, remove it from the unvalidated fields.
                // containing a value for it in the request body is valid.
                unvalidatedFields.Remove(column.Key);
            }

            // TO DO: If the request header contains x-ms-must-match custom header with value of "ignore"
            // this should not throw any error. Tracked by issue #158.
            if (unvalidatedFields.Any())
            {
                throw new DatagatewayException(
                    message: $"Invalid request body. Contained unexpected fields in body: {string.Join(", ", unvalidatedFields)}", statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Tries to get the table definition for the given entity from the configuration provider.
        /// </summary>
        /// <param name="entityName">Target entity name.</param>
        /// <param name="configurationProvider">Configuration provider that
        /// enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>

        private static TableDefinition TryGetTableDefinition(string entityName, IMetadataStoreProvider configurationProvider)
        {
            try
            {
                TableDefinition tableDefinition = configurationProvider.GetTableDefinition(entityName);
                return tableDefinition;
            }
            catch (KeyNotFoundException)
            {
                throw new DatagatewayException(
                    message: $"TableDefinition for entity: {entityName} does not exist.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Class which validates input supplied by a REST request.
    /// </summary>
    public class RequestValidator
    {
        /// <summary>
        /// Validates a FindOne request by ensuring each supplied primary key column
        /// exactly matches the DB schema.
        /// </summary>
        /// <param name="context">Request context containing request primary key columns and values</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static void ValidateFindRequest(FindRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, configurationProvider);

            foreach (string field in context.Fields)
            {
                if (!tableDefinition.Columns.ContainsKey(field))
                {
                    throw new DatagatewayException(message: "Invalid Column name: " + field, statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
                }
            }

            int primaryKeysInSchema = tableDefinition.PrimaryKey.Count;
            int primaryKeysInRequest = context.FieldValuePairs.Count;

            if (primaryKeysInRequest == 0)
            {
                // FindMany request, further primary key validation not required
                return;
            }

            if (primaryKeysInRequest != primaryKeysInSchema)
            {
                throw new DatagatewayException(message: "Primary key column(s) provided do not match DB schema.", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
            }

            //Each Predicate (Column) that is checked against the DB schema
            //is added to a list of validated columns. If a column has already
            //been checked and comes up again, the request contains duplicates.
            HashSet<string> validatedColumns = new();
            foreach (string primaryKey in context.FieldValuePairs.Keys)
            {
                if (validatedColumns.Contains(primaryKey))
                {
                    throw new DatagatewayException(message: "The request is invalid.", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);

                }

                if (!tableDefinition.PrimaryKey.Contains(primaryKey))
                {
                    throw new DatagatewayException(message: "The request is invalid.", statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
                }
                else
                {
                    validatedColumns.Add(primaryKey);
                }
            }
        }

        /// <summary>
        /// Validates an InsertOne request by ensuring each field is one of the columns in the table.
        /// </summary>
        /// <param name="context">Request context containing the insert operation fields and their values.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static JsonElement ValidatePostRequest(string queryString, string requestBody)
        {
            if (!string.IsNullOrEmpty(queryString))
            {
                throw new DatagatewayException(
                    message: "Query string for POST requests is an invalid url.",
                    statusCode: (int)HttpStatusCode.BadRequest,
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
        /// Validates an InsertOne request by ensuring each field is one of the columns in the table.
        /// </summary>
        /// <param name="context">Request context containing the insert operation fields and their values.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static void ValidateRequestContext(InsertRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, configurationProvider);

            List<string> fieldsInRequest = new(context.FieldValuePairs.Keys);
            foreach (string field in fieldsInRequest)
            {
                if (!tableDefinition.Columns.ContainsKey(field))
                {
                    // TO DO: If the request header contains x-ms-must-match custom header,
                    // this should throw an error instead.
                    context.FieldValuePairs.Remove(field);
                }
            }

            // Note: if the field value pairs do not contain values for all the primary keys,
            // either they need to be auto-generated or the database would throw error.
            // It is possible to throw exception here before going to the database,
            // if we know the unspecified primary keys cannot be autogenerated.
        }

        /// <summary>
        /// Tries to get the request by ensuring each field is one of the columns in the table.
        /// </summary>
        /// <param name="entityName">Request context containing the insert operation fields and their values.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>

        private static TableDefinition TryGetTableDefinition(string entityName, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = configurationProvider.GetTableDefinition(entityName);
            if (tableDefinition == null)
            {
                throw new DatagatewayException(
                    message: $"TableDefinition for Entity: {entityName} does not exist.",
                    statusCode: (int)HttpStatusCode.BadRequest,
                    subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
            }

            return tableDefinition;
        }

    }
}

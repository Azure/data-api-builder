// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: SqlPaginationUtil.cs
// **************************************

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Contains methods to help generating the *Connection result for pagination
    /// </summary>
    public static class SqlPaginationUtil
    {
        /// <summary>
        /// Receives the result of a query as a JsonElement and parses:
        /// <list type="bullet">
        /// <list>*Connection.items which is trivially resolved to all the elements of the result (last  discarded if hasNextPage has been requested)</list>
        /// <list>*Connection.endCursur which is the primary key of the last element of the result (last  discarded if hasNextPage has been requested)</list>
        /// <list>*Connection.hasNextPage which is decided on whether structure.Limit() elements have been returned</list>
        /// </list>
        /// </summary>
        public static JsonDocument CreatePaginationConnectionFromJsonElement(JsonElement root, PaginationMetadata paginationMetadata)
        {
            // maintains the conneciton JSON object *Connection
            Dictionary<string, object> connectionJson = new();

            IEnumerable<JsonElement> rootEnumerated = root.EnumerateArray();

            bool hasExtraElement = false;
            if (paginationMetadata.RequestedHasNextPage)
            {
                // check if the number of elements requested is successfully returned
                // structure.Limit() is first + 1 for paginated queries where hasNextPage is requested
                hasExtraElement = rootEnumerated.Count() == paginationMetadata.Structure!.Limit();

                // add hasNextPage to connection elements
                connectionJson.Add(QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME, hasExtraElement ? true : false);

                if (hasExtraElement)
                {
                    // remove the last element
                    rootEnumerated = rootEnumerated.Take(rootEnumerated.Count() - 1);
                }
            }

            int returnedElemNo = rootEnumerated.Count();

            if (paginationMetadata.RequestedItems)
            {
                if (hasExtraElement)
                {
                    // use rootEnumerated to make the *Connection.items since the last element of rootEnumerated
                    // is removed if the result has an extra element
                    connectionJson.Add(QueryBuilder.PAGINATION_FIELD_NAME, JsonSerializer.Serialize(rootEnumerated.ToArray()));
                }
                else
                {
                    // if the result doesn't have an extra element, just return the dbResult for *Conneciton.items
                    connectionJson.Add(QueryBuilder.PAGINATION_FIELD_NAME, root.ToString()!);
                }
            }

            if (paginationMetadata.RequestedEndCursor)
            {
                // parse *Connection.endCursor if there are no elements
                // if no after is added, but it has been requested HotChocolate will report it as null
                if (returnedElemNo > 0)
                {
                    JsonElement lastElemInRoot = rootEnumerated.ElementAtOrDefault(returnedElemNo - 1);
                    connectionJson.Add(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME,
                        MakeCursorFromJsonElement(
                            lastElemInRoot,
                            paginationMetadata.Structure!.PrimaryKey(),
                            paginationMetadata.Structure!.OrderByColumns,
                            paginationMetadata.Structure!.EntityName,
                            paginationMetadata.Structure!.DatabaseObject.SchemaName,
                            paginationMetadata.Structure!.DatabaseObject.Name,
                            paginationMetadata.Structure!.MetadataProvider));
                }
            }

            return JsonDocument.Parse(JsonSerializer.Serialize(connectionJson));
        }

        /// <summary>
        /// Wrapper for CreatePaginationConnectionFromJsonElement
        /// Disposes the JsonDocument passed to it
        /// <summary>
        public static JsonDocument CreatePaginationConnectionFromJsonDocument(JsonDocument jsonDocument, PaginationMetadata paginationMetadata)
        {
            // necessary for MsSql because it doesn't coalesce list query results like Postgres
            if (jsonDocument is null)
            {
                jsonDocument = JsonDocument.Parse("[]");
            }

            JsonElement root = jsonDocument.RootElement;

            // this is intentionally not disposed since it will be used for processing later
            JsonDocument result = CreatePaginationConnectionFromJsonElement(root, paginationMetadata);

            // no longer needed, so it is disposed
            jsonDocument.Dispose();

            return result;
        }

        /// <summary>
        /// Extracts the columns from the json element needed for pagination, represents them as a string in json format and base64 encodes.
        /// The JSON is encoded in base64 for opaqueness. The cursor should function as a token that the user copies and pastes
        /// without needing to understand how it works.
        /// </summary>
        public static string MakeCursorFromJsonElement(JsonElement element,
                                                        List<string> primaryKey,
                                                        List<OrderByColumn>? orderByColumns,
                                                        string entityName = "",
                                                        string schemaName = "",
                                                        string tableName = "",
                                                        ISqlMetadataProvider? sqlMetadataProvider = null)
        {
            List<PaginationColumn> cursorJson = new();
            JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            // Hash set is used here to maintain linear runtime
            // in the worst case for this function. If list is used
            // we will have in the worst case quadratic runtime.
            HashSet<string> remainingKeys = new();
            foreach (string key in primaryKey)
            {
                remainingKeys.Add(key);
            }

            // must include all orderByColumns to maintain
            // correct pagination with sorting
            if (orderByColumns is not null)
            {
                foreach (OrderByColumn column in orderByColumns)
                {
                    string? exposedColumnName = GetExposedColumnName(entityName, column.ColumnName, sqlMetadataProvider);
                    if (TryResolveJsonElementToScalarVariable(element.GetProperty(exposedColumnName), out object? value))
                    {
                        cursorJson.Add(new PaginationColumn(tableSchema: schemaName,
                                                        tableName: tableName,
                                                        exposedColumnName,
                                                        value,
                                                        tableAlias: null,
                                                        direction: column.Direction));
                    }
                    else
                    {
                        throw new DataApiBuilderException(
                            message: "Incompatible data to create pagination cursor.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorProcessingData);
                    }

                    remainingKeys.Remove(column.ColumnName);
                }
            }

            // primary key columns are used in ordering
            // for tie-breaking and must be included.
            // iterate through primary key list and check if
            // each column is in the set of remaining primary
            // keys, add to cursor any columns that are in the
            // set of remaining primary keys and then remove that
            // column from this remaining key set. This way we
            // iterate in the same order as the ordering of the
            // primary key columns, and verify all primary key
            // columns have been added to the cursor.
            foreach (string column in primaryKey)
            {
                if (remainingKeys.Contains(column))
                {
                    string? exposedColumnName = GetExposedColumnName(entityName, column, sqlMetadataProvider);
                    if (TryResolveJsonElementToScalarVariable(element.GetProperty(column), out object? value))
                    {
                        cursorJson.Add(new PaginationColumn(tableSchema: schemaName,
                                                        tableName: tableName,
                                                        exposedColumnName,
                                                        value,
                                                        direction: OrderBy.ASC));
                    }
                    else
                    {
                        throw new DataApiBuilderException(
                           message: "Incompatible data to create pagination cursor.",
                           statusCode: HttpStatusCode.BadRequest,
                           subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorProcessingData);
                    }

                    remainingKeys.Remove(column);
                }
            }

            return Base64Encode(JsonSerializer.Serialize(cursorJson, options));
        }

        /// <summary>
        /// Parse the value of "after" parameter from query parameters, validate it, and return the json object it stores
        /// </summary>
        public static IEnumerable<PaginationColumn> ParseAfterFromQueryParams(
            IDictionary<string, object?> queryParams,
            PaginationMetadata paginationMetadata,
            ISqlMetadataProvider sqlMetadataProvider,
            string EntityName,
            RuntimeConfigProvider runtimeConfigProvider)
        {
            if (queryParams.TryGetValue(QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME, out object? continuationObject))
            {
                if (continuationObject is not null)
                {
                    string afterPlainText = (string)continuationObject;
                    return ParseAfterFromJsonString(
                        afterPlainText,
                        paginationMetadata,
                        sqlMetadataProvider,
                        EntityName,
                        runtimeConfigProvider);
                }
            }

            return Enumerable.Empty<PaginationColumn>();
        }

        /// <summary>
        /// Validate the value associated with $after, and return list of orderby columns
        /// it represents.
        /// </summary>
        public static IEnumerable<PaginationColumn> ParseAfterFromJsonString(string afterJsonString,
                                                                             PaginationMetadata paginationMetadata,
                                                                             ISqlMetadataProvider sqlMetadataProvider,
                                                                             string entityName,
                                                                             RuntimeConfigProvider runtimeConfigProvider
                                                                             )
        {
            IEnumerable<PaginationColumn>? after;
            try
            {
                afterJsonString = Base64Decode(afterJsonString);
                after = JsonSerializer.Deserialize<IEnumerable<PaginationColumn>>(afterJsonString);

                if (after is null)
                {
                    throw new ArgumentException("Failed to parse the pagination information from the provided token");
                }

                Dictionary<string, PaginationColumn> afterDict = new();
                foreach (PaginationColumn column in after)
                {
                    // REST calls this function with a non null sqlMetadataProvider
                    // which will get the exposed name for safe messaging in the response.
                    // Since we are looking for pagination columns from the $after query
                    // param, we expect this column to exist as the $after query param
                    // was formed from a previous response with a nextLink. If the nextLink
                    // has been modified and backingColumn is null we throw exception.
                    string backingColumnName = GetBackingColumnName(entityName, column.ColumnName, sqlMetadataProvider);
                    if (backingColumnName is null)
                    {
                        throw new DataApiBuilderException(message: $"Cursor for Pagination Predicates is not well formed, {column.ColumnName} is not valid.",
                                                       statusCode: HttpStatusCode.BadRequest,
                                                       subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    // holds exposed name mapped to exposed pagination column
                    afterDict.Add(column.ColumnName, column);
                    // overwrite with backing column's name for query generation
                    column.ColumnName = backingColumnName;
                }

                // verify that primary keys is a sub set of after's column names
                // if any primary keys are not contained in after's column names we throw exception
                List<string> primaryKeys = paginationMetadata.Structure!.PrimaryKey();

                foreach (string pk in primaryKeys)
                {
                    // REST calls this function with a non null sqlMetadataProvider
                    // which will get the exposed name for safe messaging in the response.
                    // Since we are looking for primary keys we expect these columns to
                    // exist.
                    string safePK = GetExposedColumnName(entityName, pk, sqlMetadataProvider);
                    if (!afterDict.ContainsKey(safePK))
                    {
                        throw new DataApiBuilderException(message: $"Cursor for Pagination Predicates is not well formed, missing primary key column: {safePK}",
                                                       statusCode: HttpStatusCode.BadRequest,
                                                       subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                }

                // verify that orderby columns for the structure and the after columns
                // match in name and direction
                int orderByColumnCount = 0;
                SqlQueryStructure structure = paginationMetadata.Structure!;
                foreach (OrderByColumn column in structure.OrderByColumns)
                {
                    string columnName = GetExposedColumnName(entityName, column.ColumnName, sqlMetadataProvider);

                    if (!afterDict.ContainsKey(columnName) ||
                        afterDict[columnName].Direction != column.Direction)
                    {
                        // REST calls this function with a non null sqlMetadataProvider
                        // which will get the exposed name for safe messaging in the response.
                        // Since we are looking for valid orderby columns we expect
                        // these columns to exist.
                        string safeColumnName = GetExposedColumnName(entityName, columnName, sqlMetadataProvider);
                        throw new DataApiBuilderException(
                                    message: $"Could not match order by column {safeColumnName} with a column in the pagination token with the same name and direction.",
                                    statusCode: HttpStatusCode.BadRequest,
                                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    orderByColumnCount++;
                }

                // the check above validates that all orderby columns are matched with after columns
                // also validate that there are no extra after columns
                if (afterDict.Count != orderByColumnCount)
                {
                    throw new ArgumentException("After token contains extra columns not present in order by columns.");
                }
            }
            catch (Exception e) when (
                e is InvalidCastException ||
                e is ArgumentException ||
                e is ArgumentNullException ||
                e is FormatException ||
                e is System.Text.DecoderFallbackException ||
                e is JsonException ||
                e is NotSupportedException
                )
            {
                // Possible sources of exceptions:
                // stringObject cannot be converted to string
                // afterPlainText cannot be successfully decoded
                // afterJsonString cannot be deserialized
                // keys of afterDeserialized do not correspond to the primary key
                // values given for the primary keys are of incorrect format
                // duplicate column names in the after token and / or the orderby columns
                string errorMessage = runtimeConfigProvider.IsDeveloperMode() ? $"{e.Message}\n{e.StackTrace}" :
                    $"{afterJsonString} is not a valid pagination token.";
                throw new DataApiBuilderException(
                    message: errorMessage,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: e);
            }

            return after;
        }

        /// <summary>
        /// Helper function will return the backing column name, which is
        /// what is used to form pagination columns in the query.
        /// </summary>
        /// <param name="entityName">String holds the name of the entity.</param>
        /// <param name="exposedColumnName">String holds the name of the exposed column.</param>
        /// <param name="sqlMetadataProvider">Holds the sqlmetadataprovider for REST requests,
        /// which provides mechanisms to resolve exposedName -> backingColumnName and
        /// backingColumnName -> exposedName.</param>
        /// <returns>the backing column name.</returns>
        /// <returns></returns>
        private static string GetBackingColumnName(string entityName, string exposedColumnName, ISqlMetadataProvider? sqlMetadataProvider)
        {
            if (sqlMetadataProvider is not null)
            {
                sqlMetadataProvider.TryGetBackingColumn(entityName, exposedColumnName, out exposedColumnName!);
            }

            return exposedColumnName;
        }

        /// <summary>
        /// Helper function will return the exposed column name, which is
        /// what is used to return a cursor in the response, since we only
        /// use the exposed names in requests and responses.
        /// </summary>
        /// <param name="entityName">String holds the name of the entity.</param>
        /// <param name="backingColumn">String holds the name of the backing column.</param>
        /// <param name="sqlMetadataProvider">Holds the sqlmetadataprovider for REST requests.</param>
        /// <returns>the exposed name</returns>
        private static string GetExposedColumnName(string entityName, string backingColumn, ISqlMetadataProvider? sqlMetadataProvider)
        {
            if (sqlMetadataProvider is not null)
            {
                sqlMetadataProvider.TryGetExposedColumnName(entityName, backingColumn, out backingColumn!);
            }

            return backingColumn;
        }

        /// <summary>
        /// Tries to resolve a JsonElement representing a variable to the appropriate type
        /// </summary>
        /// <param name="element">The Json element to convert from.</param>
        /// <param name="scalarVariable">The scalar into which the element is resolved based on its ValueKind.</param>
        /// <returns>True when resolution is successful, false otherwise.</returns>
        public static bool TryResolveJsonElementToScalarVariable(
            JsonElement element,
            out object? scalarVariable)
        {
            bool resolved = true;
            scalarVariable = null;
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                {
                    scalarVariable = element.GetString();
                    break;
                }

                case JsonValueKind.Number:
                {
                    if (element.TryGetDouble(out double value))
                    {
                        scalarVariable = value;
                    }

                    break;
                }

                case JsonValueKind.Null:
                {
                    scalarVariable = null;
                    break;
                }

                case JsonValueKind.True:
                {
                    scalarVariable = true;
                    break;
                }

                case JsonValueKind.False:
                {
                    scalarVariable = false;
                    break;
                }

                default:
                {
                    resolved = false;
                    break;
                }
            }

            return resolved;
        }

        /// <summary>
        /// Encodes string to base64
        /// </summary>
        public static string Base64Encode(string plainText)
        {
            byte[] plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        /// <summary>
        /// Decode base64 string to plain text
        /// </summary>
        public static string Base64Decode(string base64EncodedData)
        {
            byte[] base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        /// <summary>
        /// Create the URL that will provide for the next page of results
        /// using the same query options.
        /// </summary>
        /// <param name="path">The request path.</param>
        /// <param name="nvc">Collection of query params.</param>
        /// <param name="after">The values needed for next page.</param>
        /// <returns>The string representing nextLink.</returns>
        public static JsonElement CreateNextLink(string path, NameValueCollection? nvc, string after)
        {
            if (nvc is null)
            {
                nvc = new();
            }

            if (!string.IsNullOrWhiteSpace(after))
            {
                nvc["$after"] = after;
            }

            // ValueKind will be array so we can differentiate from other objects in the response
            // to be returned.
            string jsonString = JsonSerializer.Serialize(new[]
            {
                new
                {
                    nextLink = @$"{path}?{nvc}"
                }
            });
            return JsonSerializer.Deserialize<JsonElement>(jsonString);
        }

        /// <summary>
        /// Returns true if the table has more records that
        /// match the query options than were requested.
        /// </summary>
        /// <param name="jsonResult">Results plus one extra record if more exist.</param>
        /// <param name="first">Client provided limit if one exists, otherwise 0.</param>
        /// <returns>Bool representing if more records are available.</returns>
        public static bool HasNext(JsonElement jsonResult, uint? first)
        {
            // When first is 0 we use default limit of 100, otherwise we use first
            uint numRecords = (uint)jsonResult.GetArrayLength();
            uint? limit = first is not null ? first : 100;
            return numRecords > limit;
        }
    }
}

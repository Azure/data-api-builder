// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using QueryBuilder = Azure.DataApiBuilder.Service.GraphQLBuilder.Queries.QueryBuilder;

namespace Azure.DataApiBuilder.Core.Resolvers
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
        /// <list>*Connection.endCursor which is the primary key of the last element of the result (last  discarded if hasNextPage has been requested)</list>
        /// <list>*Connection.hasNextPage which is decided on whether structure.Limit() elements have been returned</list>
        /// </list>
        /// </summary>
        public static JsonElement CreatePaginationConnectionFromJsonElement(JsonElement root, PaginationMetadata paginationMetadata)
            => CreatePaginationConnection(root, paginationMetadata).ToJsonElement();

        /// <summary>
        /// Wrapper for CreatePaginationConnectionFromJsonElement
        /// </summary>
        public static JsonDocument CreatePaginationConnectionFromJsonDocument(JsonDocument? jsonDocument, PaginationMetadata paginationMetadata, GroupByMetadata? groupByMetadata = null)
        {
            // necessary for MsSql because it doesn't coalesce list query results like Postgres
            if (jsonDocument is null)
            {
                jsonDocument = JsonDocument.Parse("[]");
            }

            JsonElement root = jsonDocument.RootElement.Clone();

            // create the connection object.
            return CreatePaginationConnection(root, paginationMetadata, groupByMetadata).ToJsonDocument();
        }

        private static string GenerateGroupByObjectFromResult(GroupByMetadata groupByMetadata, IEnumerable<JsonElement> rootEnumerated)
        {
            JsonArray groupByArray = new();
            foreach (JsonElement element in rootEnumerated)
            {
                JsonObject fieldObject = new();
                JsonObject aggregationObject = new();
                JsonObject combinedObject = new();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (groupByMetadata.Fields.ContainsKey(property.Name))
                    {
                        if (groupByMetadata.RequestedFields)
                        {
                            fieldObject.Add(property.Name, JsonNode.Parse(property.Value.GetRawText()));
                        }
                    }
                    else
                    {
                        aggregationObject.Add(property.Name, JsonNode.Parse(property.Value.GetRawText()));
                    }
                }

                combinedObject.Add(QueryBuilder.GROUP_BY_FIELDS_FIELD_NAME, fieldObject);
                combinedObject.Add(QueryBuilder.GROUP_BY_AGGREGATE_FIELD_NAME, aggregationObject);
                groupByArray.Add(combinedObject);
            }

            return JsonSerializer.Serialize(groupByArray);
        }

        private static JsonObject CreatePaginationConnection(JsonElement root, PaginationMetadata paginationMetadata, GroupByMetadata? groupByMetadata = null)
        {
            // Maintains the connection JSON object *Connection
            JsonObject connection = new();

            // in dw we wrap array with "" and hence jsonValueKind is string instead of array.
            if (root.ValueKind is JsonValueKind.String)
            {
                using JsonDocument document = JsonDocument.Parse(root.GetString()!);
                root = document.RootElement.Clone();
            }

            // If the request includes either hasNextPage or endCursor then to correctly return those
            // values we need to determine the correct pagination logic
            bool isPaginationRequested = paginationMetadata.RequestedHasNextPage || paginationMetadata.RequestedEndCursor;

            IEnumerable<JsonElement> rootEnumerated = root.EnumerateArray();
            int returnedElementCount = rootEnumerated.Count();
            bool hasExtraElement = false;

            if (isPaginationRequested)
            {
                // structure.Limit() is first + 1 for paginated queries where hasNextPage or endCursor is requested
                hasExtraElement = returnedElementCount == paginationMetadata.Structure!.Limit();
                if (hasExtraElement)
                {
                    // In a pagination scenario where we have an extra element, this element
                    // must be removed since it was only used to determine if there are additional
                    // records after those requested.
                    rootEnumerated = rootEnumerated.Take(rootEnumerated.Count() - 1);
                    --returnedElementCount;
                }
            }

            if (paginationMetadata.RequestedHasNextPage)
            {
                // add hasNextPage to connection elements
                connection.Add(QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME, hasExtraElement);
            }

            if (paginationMetadata.RequestedItems)
            {
                if (hasExtraElement)
                {
                    // use rootEnumerated to make the *Connection.items since the last element of rootEnumerated
                    // is removed if the result has an extra element
                    connection.Add(QueryBuilder.PAGINATION_FIELD_NAME, JsonSerializer.Serialize(rootEnumerated.ToArray()));
                }
                else
                {
                    // if the result doesn't have an extra element, just return the dbResult for *Connection.items
                    connection.Add(QueryBuilder.PAGINATION_FIELD_NAME, root.ToString()!);
                }
            }

            if (groupByMetadata is not null && paginationMetadata.RequestedGroupBy == true)
            {

                connection.Add(QueryBuilder.GROUP_BY_FIELD_NAME, GenerateGroupByObjectFromResult(groupByMetadata, rootEnumerated));
            }

            if (paginationMetadata.RequestedEndCursor)
            {
                // Note: if we do not add endCursor to the connection but it was in the request, its value will
                // automatically be populated as null.
                // Need to validate we have an extra element, because otherwise there is no next page
                // and endCursor should be left as null.
                if (returnedElementCount > 0 && hasExtraElement)
                {
                    JsonElement lastElemInRoot = rootEnumerated.ElementAtOrDefault(returnedElementCount - 1);
                    connection.Add(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME,
                        MakeCursorFromJsonElement(
                            lastElemInRoot,
                            paginationMetadata.Structure!.PrimaryKey(),
                            paginationMetadata.Structure!.OrderByColumns,
                            paginationMetadata.Structure!.EntityName,
                            paginationMetadata.Structure!.DatabaseObject.SchemaName,
                            paginationMetadata.Structure!.DatabaseObject.Name,
                            paginationMetadata.Structure!.MetadataProvider,
                            paginationMetadata.RequestedGroupBy));
                }
            }

            return connection;
        }

        /// <summary>
        /// Holds the information safe to expose in the response's pagination cursor,
        /// the NextLink. The NextLink column represents the safe to expose information
        /// that defines the entity, field, field value, and direction of sorting to
        /// continue to the next page. These can then be used to form the pagination
        /// columns that will be needed for the actual query.
        /// </summary>
        private class NextLinkField
        {
            public string EntityName { get; set; }
            public string FieldName { get; set; }
            public object? FieldValue { get; }
            public string? ParamName { get; set; }
            public OrderBy Direction { get; set; }

            public NextLinkField(
                string entityName,
                string fieldName,
                object? fieldValue,
                string? paramName = null,
                // default sorting direction is ascending so we maintain that convention
                OrderBy direction = OrderBy.ASC)
            {
                EntityName = entityName;
                FieldName = fieldName;
                FieldValue = fieldValue;
                ParamName = paramName;
                Direction = direction;
            }
        }

        /// <summary>
        /// Extracts the columns from the JsonElement needed for pagination, represents them as a string in json format and base64 encodes.
        /// The JSON is encoded in base64 for opaqueness. The cursor should function as a token that the user copies and pastes
        /// without needing to understand how it works.
        /// </summary>
        public static string MakeCursorFromJsonElement(
            JsonElement element,
            List<string> primaryKey,
            List<OrderByColumn>? orderByColumns,
            string entityName = "",
            string schemaName = "",
            string tableName = "",
            ISqlMetadataProvider? sqlMetadataProvider = null,
            bool isGroupByQuery = false)
        {
            List<NextLinkField> cursorJson = new();
            JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            // Hash set is used here to maintain linear runtime
            // in the worst case for this function. If list is used
            // we will have in the worst case quadratic runtime.
            HashSet<string> remainingKeys = new();

            if (!isGroupByQuery)
            {
                foreach (string key in primaryKey)
                {
                    remainingKeys.Add(key);
                }
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
                        cursorJson.Add(new NextLinkField(
                            entityName: entityName,
                            fieldName: exposedColumnName,
                            fieldValue: value,
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

            // Primary key columns must be included in the orderBy query parameter in the nextLink cursor to break ties between result set records.
            // Iterate through list of (composite) primary key(s) and when a primary key column exists in the remaining keys collection:
            // 1.) Add that column as one of the pagination columns in the orderBy query parameter in the generated nextLink cursor.
            // 2.) Remove the column from the remaining keys collection.
            // This loop enables consistent iteration over the list of primary key columns which:
            // - Maintains the order of the primary key columns as they exist in the database.
            // - Ensures all primary key columns have been added to the nextLink cursor.
            foreach (string column in primaryKey)
            {
                if (remainingKeys.Contains(column))
                {
                    string? exposedColumnName = GetExposedColumnName(entityName, column, sqlMetadataProvider);
                    if (TryResolveJsonElementToScalarVariable(element.GetProperty(exposedColumnName), out object? value))
                    {
                        cursorJson.Add(new NextLinkField(
                            entityName: entityName,
                            fieldName: exposedColumnName,
                            fieldValue: value));
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
        public static IEnumerable<PaginationColumn> ParseAfterFromJsonString(
            string afterJsonString,
            PaginationMetadata paginationMetadata,
            ISqlMetadataProvider sqlMetadataProvider,
            string entityName,
            RuntimeConfigProvider runtimeConfigProvider
            )
        {
            List<PaginationColumn>? paginationCursorColumnsForQuery = new();
            IEnumerable<NextLinkField>? paginationCursorFieldsFromRequest;
            try
            {
                afterJsonString = Base64Decode(afterJsonString);
                paginationCursorFieldsFromRequest = JsonSerializer.Deserialize<IEnumerable<NextLinkField>>(afterJsonString);

                if (paginationCursorFieldsFromRequest is null)
                {
                    throw new ArgumentException("Failed to parse the pagination information from the provided token");
                }

                Dictionary<string, PaginationColumn> exposedFieldNameToBackingColumn = new();
                foreach (NextLinkField field in paginationCursorFieldsFromRequest)
                {
                    // REST calls this function with a non null sqlMetadataProvider
                    // which will get the exposed name for safe messaging in the response.
                    // Since we are looking for pagination columns from the $after query
                    // param, we expect this column to exist as the $after query param
                    // was formed from a previous response with a nextLink. If the nextLink
                    // has been modified and backingColumn is null we throw exception.
                    string backingColumnName = GetBackingColumnName(entityName, field.FieldName, sqlMetadataProvider);
                    if (backingColumnName is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"Pagination token is not well formed because {field.FieldName} is not valid.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    PaginationColumn pageColumn = new(
                        tableName: "",
                        tableSchema: "",
                        columnName: backingColumnName,
                        value: field.FieldValue,
                        paramName: field.ParamName,
                        direction: field.Direction);
                    paginationCursorColumnsForQuery.Add(pageColumn);
                    // holds exposed name mapped to exposed pagination column
                    exposedFieldNameToBackingColumn.Add(field.FieldName, pageColumn);
                }

                // verify that primary keys is a sub set of after's column names
                // if any primary keys are not contained in after's column names we throw exception
                List<string> primaryKeys = paginationMetadata.Structure!.PrimaryKey();

                if (!paginationMetadata.RequestedGroupBy)
                {
                    // primary key not valid check for groupby ordering.
                    foreach (string pk in primaryKeys)
                    {
                        // REST calls this function with a non null sqlMetadataProvider
                        // which will get the exposed name for safe messaging in the response.
                        // Since we are looking for primary keys we expect these columns to
                        // exist.
                        string exposedFieldName = GetExposedColumnName(entityName, pk, sqlMetadataProvider);
                        if (!exposedFieldNameToBackingColumn.ContainsKey(exposedFieldName))
                        {
                            throw new DataApiBuilderException(
                                message: $"Pagination token is not well formed because it is missing an expected field: {exposedFieldName}",
                                statusCode: HttpStatusCode.BadRequest,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                        }
                    }
                }

                // verify that orderby columns for the structure and the after columns
                // match in name and direction
                int orderByColumnCount = 0;
                SqlQueryStructure structure = paginationMetadata.Structure!;
                foreach (OrderByColumn column in structure.OrderByColumns)
                {
                    string exposedFieldName = GetExposedColumnName(entityName, column.ColumnName, sqlMetadataProvider);

                    if (!exposedFieldNameToBackingColumn.ContainsKey(exposedFieldName) ||
                        exposedFieldNameToBackingColumn[exposedFieldName].Direction != column.Direction)
                    {
                        // REST calls this function with a non null sqlMetadataProvider
                        // which will get the exposed name for safe messaging in the response.
                        // Since we are looking for valid orderby columns we expect
                        // these columns to exist.
                        string exposedOrderByFieldName = GetExposedColumnName(entityName, column.ColumnName, sqlMetadataProvider);
                        throw new DataApiBuilderException(
                            message: $"Could not match order by column {exposedOrderByFieldName} with a column in the pagination token with the same name and direction.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    orderByColumnCount++;
                }

                // the check above validates that all orderby columns are matched with after columns
                // also validate that there are no extra after columns
                if (exposedFieldNameToBackingColumn.Count != orderByColumnCount)
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
                string errorMessage = runtimeConfigProvider.GetConfig().IsDevelopmentMode() ? $"{e.Message}\n{e.StackTrace}" :
                    $"{afterJsonString} is not a valid pagination token.";
                throw new DataApiBuilderException(
                    message: errorMessage,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: e);
            }

            return paginationCursorColumnsForQuery;
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
            return Convert.ToBase64String(plainTextBytes);
        }

        /// <summary>
        /// Decode base64 string to plain text
        /// </summary>
        public static string Base64Decode(string base64EncodedData)
        {
            byte[] base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        /// <summary>
        /// Constructs the base Uri for Pagination
        /// </summary>
        /// <remarks>
        /// This method uses the "X-Forwarded-Proto" and "X-Forwarded-Host" headers to determine
        /// the scheme and host of the request, falling back to the request's original scheme and host if the headers
        /// are not present or invalid. The method ensures that the scheme is either "http" or "https" and that the host
        /// is a valid hostname or IP address.
        /// </remarks>
        /// <param name="httpContext">The HTTP context containing the request information.</param>
        /// <param name="baseRoute">An optional base route to prepend to the request path. If not specified, no base route is used.</param>
        /// <returns>A string representing the fully constructed Base request URL for Pagination.</returns>
        public static string ConstructBaseUriForPagination(HttpContext httpContext, string? baseRoute = null)
        {
            HttpRequest req = httpContext.Request;

            // use scheme from X-Forwarded-Proto or fallback to request scheme
            string scheme = ResolveRequestScheme(req);

            // Use host from X-Forwarded-Host or fallback to request host
            string host = ResolveRequestHost(req);

            // If the base route is not empty, we need to insert it into the URI before the rest path.
            // Path is of the form ....restPath/pathNameForEntity. We want to insert the base route before the restPath.
            // Finally, it will be of the form: .../baseRoute/restPath/pathNameForEntity.
            return UriHelper.BuildAbsolute(
                scheme: scheme,
                host: new HostString(host),
                pathBase: string.IsNullOrWhiteSpace(baseRoute) ? PathString.Empty : new PathString(baseRoute),
                path: req.Path);
        }

        /// <summary>
        /// Builds a query string by appending or replacing the <c>$after</c> token with the specified value.
        /// </summary>
        /// <remarks>This method does not include the <paramref name="path"/> in the returned query
        /// string. It only processes and formats the query string parameters.</remarks>
        /// <param name="queryStringParameters">A collection of existing query string parameters. If <see langword="null"/>, an empty collection is used.
        /// The <c>$after</c> parameter, if present, will be removed before appending the new token.</param>
        /// <param name="newAfterPayload">The new value for the <c>$after</c> token. If this value is <see langword="null"/>, empty, or whitespace, no
        /// <c>$after</c> token will be appended.</param>
        /// <returns>A URL-encoded query string containing the updated parameters, including the new <c>$after</c> token if
        /// specified. If no parameters are provided and <paramref name="newAfterPayload"/> is empty, an empty string is
        /// returned.</returns>
        public static string BuildQueryStringWithAfterToken(NameValueCollection? queryStringParameters, string newAfterPayload)
        {
            if (queryStringParameters is null)
            {
                queryStringParameters = new();
            }
            else
            {
                queryStringParameters.Remove("$after");
            }

            // Format existing query string (URL encoded)
            string queryString = FormatQueryString(queryStringParameters);

            // Append new $after token
            if (!string.IsNullOrWhiteSpace(newAfterPayload))
            {
                string afterPrefix = string.IsNullOrWhiteSpace(queryString) ? "?" : "&";
                queryString += $"{afterPrefix}{RequestParser.AFTER_URL}={newAfterPayload}";
            }

            // Construct final link
            // return $"{path}{queryString}";
            return queryString;
        }

        /// <summary>
        /// Gets a consolidated next link for pagination in JSON format.
        /// </summary>
        /// <param name="baseUri">The base Pagination Uri</param>
        /// <param name="queryString">The query string with after value</param>
        /// <param name="isNextLinkRelative">True, if the next link should be relative</param>
        /// <returns></returns>
        public static JsonElement GetConsolidatedNextLinkForPagination(string baseUri, string queryString, bool isNextLinkRelative = false)
        {
            UriBuilder uriBuilder = new(baseUri)
            {
                // Form final link by appending the query string
                Query = queryString
            };

            // Construct final link- absolute or relative
            string nextLinkValue = isNextLinkRelative
                ? uriBuilder.Uri.PathAndQuery // returns just "/api/<Entity>?$after...", no host
                : uriBuilder.Uri.AbsoluteUri; // returns full URL

            // Return serialized JSON object
            string jsonString = JsonSerializer.Serialize(new[]
            {
                new { nextLink = nextLinkValue }
            });

            return JsonSerializer.Deserialize<JsonElement>(jsonString);
        }

        /// <summary>
        /// Returns true if the table has more records that
        /// match the query options than were requested.
        /// </summary>
        /// <param name="jsonResult">Results plus one extra record if more exist.</param>
        /// <param name="first">Client provided limit if one exists, otherwise 0.</param>
        /// <param name="defaultPageSize">Default limit for page size.</param>
        /// <param name="maxPageSize">Maximum limit for page size.</param>
        /// <returns>Bool representing if more records are available.</returns>
        public static bool HasNext(JsonElement jsonResult, int? first, uint defaultPageSize, uint maxPageSize)
        {
            // When first is null we use default limit from runtime config, otherwise we use first
            uint numRecords = (uint)jsonResult.GetArrayLength();

            uint limit;
            if (first.HasValue)
            {
                // first is not null.
                if (first == -1)
                {
                    // user has requested max value.
                    limit = maxPageSize;
                }
                else
                {
                    limit = (uint)first;
                }
            }
            else
            {
                limit = defaultPageSize;
            }

            return numRecords > limit;
        }

        /// <summary>
        /// Creates a uri encoded query string from a NameValueCollection using .NET QueryHelpers.
        /// Addresses the limitations:
        /// 1) NameValueCollection is not resolved as string in JSON serialization.
        /// 2) NameValueCollection keys and values are not URL escaped.
        /// </summary>
        /// <param name="queryStringParameters">Key: $QueryStringParamKey Value: QueryStringParamValue</param>
        /// <returns>Query string prefixed with question mark (?). Returns an empty string when
        /// no entries exist in queryStringParameters.</returns>
        public static string FormatQueryString(NameValueCollection? queryStringParameters)
        {
            string queryString = "";
            if (queryStringParameters is null || queryStringParameters.Count is 0)
            {
                return queryString;
            }

            foreach (string key in queryStringParameters)
            {
                // Whitespace or empty string query paramters are not supported.
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                // There may be duplicate query string parameter keys, so get
                // all values associated to given key in a comma-separated list
                // format compatible with OData expression syntax.
                string? queryStringParamValues = queryStringParameters.Get(key);

                if (!string.IsNullOrWhiteSpace(queryStringParamValues))
                {
                    // AddQueryString will URI encode the returned string which may
                    // interfere with other encodings, ie: base64 encoding used for
                    // the "after" parameter's value.
                    queryString = QueryHelpers.AddQueryString(queryString, key, queryStringParamValues);
                }
            }

            return queryString;
        }

        /// <summary>
        /// Extracts and request scheme from "X-Forwarded-Proto" or falls back to the request scheme.
        /// </summary>
        /// <param name="req">The HTTP request.</param>
        /// <returns>The scheme string ("http" or "https").</returns>
        /// <exception cref="DataApiBuilderException">Thrown when client explicitly sets an invalid scheme.</exception>
        private static string ResolveRequestScheme(HttpRequest req)
        {
            string? rawScheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault();
            string? normalized = rawScheme?.Trim().ToLowerInvariant();

            bool isExplicit = !string.IsNullOrEmpty(rawScheme);
            bool isValid = IsValidScheme(normalized);

            if (isExplicit && !isValid)
            {
                // Log a warning and ignore the invalid value, fallback to request's scheme
                Console.WriteLine($"Warning: Invalid scheme '{rawScheme}' in X-Forwarded-Proto header. Falling back to request scheme: '{req.Scheme}'.");
                return req.Scheme;
            }

            return isValid ? normalized! : req.Scheme;
        }

        /// <summary>
        /// Extracts the request host from "X-Forwarded-Host" or falls back to the request host.
        /// </summary>
        /// <param name="req">The HTTP request.</param>
        /// <returns>The host string.</returns>
        /// <exception cref="DataApiBuilderException">Thrown when client explicitly sets an invalid host.</exception>
        private static string ResolveRequestHost(HttpRequest req)
        {
            string? rawHost = req.Headers["X-Forwarded-Host"].FirstOrDefault();
            string? trimmed = rawHost?.Trim();

            bool isExplicit = !string.IsNullOrEmpty(rawHost);
            bool isValid = IsValidHost(trimmed);

            if (isExplicit && !isValid)
            {
                // Log a warning and ignore the invalid value, fallback to request's host
                Console.WriteLine($"Warning: Invalid host '{rawHost}' in X-Forwarded-Host header. Falling back to request host: '{req.Host}'.");
                return req.Host.ToString();
            }

            return isValid ? trimmed! : req.Host.ToString();
        }

        /// <summary>
        /// Checks if the provided scheme is valid.
        /// </summary>
        /// <param name="scheme">Scheme, e.g., "http" or "https".</param>
        /// <returns>True if valid, otherwise false.</returns>
        private static bool IsValidScheme(string? scheme)
        {
            return scheme is "http" or "https";
        }

        /// <summary>
        /// Checks if the provided host is a valid hostname or IP address.
        /// </summary>
        /// <param name="host">The host name (with optional port).</param>
        /// <returns>True if valid, otherwise false.</returns>
        private static bool IsValidHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            // Reject dangerous characters
            if (host.Contains('\r') || host.Contains('\n') || host.Contains(' ') ||
                host.Contains('<') || host.Contains('>') || host.Contains('@'))
            {
                return false;
            }

            // Validate host part (exclude port if present)
            string hostnamePart = host.Split(':')[0];

            if (Uri.CheckHostName(hostnamePart) == UriHostNameType.Unknown)
            {
                return false;
            }

            // Final sanity check: ensure it parses into a full URI
            return Uri.TryCreate($"http://{host}", UriKind.Absolute, out _);
        }
    }
}

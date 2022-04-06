using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Contains methods to help generating the *Connection result for pagination
    /// </summary>
    public class SqlPaginationUtil
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
                connectionJson.Add("hasNextPage", hasExtraElement ? true : false);

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
                    connectionJson.Add("items", JsonSerializer.Serialize(rootEnumerated.ToArray()));
                }
                else
                {
                    // if the result doesn't have an extra element, just return the dbResult for *Conneciton.items
                    connectionJson.Add("items", root.ToString()!);
                }
            }

            if (paginationMetadata.RequestedEndCursor)
            {
                // parse *Connection.endCursor if there are no elements
                // if no endCursor is added, but it has been requested HotChocolate will report it as null
                if (returnedElemNo > 0)
                {
                    JsonElement lastElemInRoot = rootEnumerated.ElementAtOrDefault(returnedElemNo - 1);
                    connectionJson.Add("endCursor", MakeCursorFromJsonElement(lastElemInRoot, paginationMetadata.Structure!.PrimaryKey()));
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
            if (jsonDocument == null)
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
        /// Wrapper function ensures that we call into the 4 parameter function
        /// with both nextElement and orderByColumns default/null.
        /// </summary>
        public static string MakeCursorFromJsonElement(JsonElement element, List<string> primaryKey)
        {
            return MakeCursorFromJsonElement(element, primaryKey, nextElement: default, orderByColumns: null);
        }

        /// <summary>
        /// Extracts the columns from the json element needed for pagination, represents them as a string in json format and base64 encodes.
        /// The JSON is encoded in base64 for opaqueness. The cursor should function as a token that the user copies and pastes
        /// without needing to understand how it works.
        /// </summary>
        public static string MakeCursorFromJsonElement(JsonElement element, List<string> primaryKey, JsonElement nextElement, List<Column>? orderByColumns)
        {
            Dictionary<string, object[]> cursorJson = new();
            // If we have orderByColumns need to check if any of these
            // columns are not tied between element and nextElement
            // in which case the first non-tie will determine pagination order
            if (orderByColumns is not null)
            {
                foreach (Column column in orderByColumns)
                {
                    object value = ResolveJsonElementToScalarVariable(element.GetProperty(column.ColumnName));
                    object nextValue = ResolveJsonElementToScalarVariable(nextElement.GetProperty(column.ColumnName));

                    if (!value.Equals(nextValue))
                    {
                        cursorJson.Add(column.ColumnName, new object[] { ResolveJsonElementToScalarVariable(element.GetProperty(column.ColumnName)), (column as OrderByColumn)!.Direction });
                        return Base64Encode(JsonSerializer.Serialize(cursorJson));
                    }
                }
            }

            foreach (string column in primaryKey)
            {
                cursorJson.Add(column, new object[] { ResolveJsonElementToScalarVariable(element.GetProperty(column)), "Asc" });
            }

            return Base64Encode(JsonSerializer.Serialize(cursorJson));
        }

        /// <summary>
        /// Parse the value of "after" parameter from query parameters, validate it, and return the json object it stores
        /// </summary>
        public static IDictionary<string, object[]> ParseAfterFromQueryParams(IDictionary<string, object> queryParams, PaginationMetadata paginationMetadata)
        {
            Dictionary<string, object[]> after = new();
            object afterObject = queryParams["after"];

            if (afterObject != null)
            {
                string afterPlainText = (string)afterObject;
                after = ParseAfterFromJsonString(afterPlainText, paginationMetadata);

            }

            return after;
        }

        /// <summary>
        /// Validate the value associated with $after, and return the json object it stores
        /// </summary>
        public static Dictionary<string, object[]> ParseAfterFromJsonString(string afterJsonString, PaginationMetadata paginationMetadata)
        {
            Dictionary<string, object[]> after = new();
            //List<string> primaryKey = paginationMetadata.Structure!.PrimaryKey();

            try
            {
                afterJsonString = Base64Decode(afterJsonString);
                Dictionary<string, JsonElement> afterDeserialized = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(afterJsonString)!;

                foreach (KeyValuePair<string, JsonElement> keyValuePair in afterDeserialized)
                {
                    IEnumerable<JsonElement> enumeratedValue = keyValuePair.Value.EnumerateArray();
                    object value = ResolveJsonElementToScalarVariable(enumeratedValue.First());
                    object direction = enumeratedValue.Last();
                    object[] valueAndDirection = { value, direction };

                    Type columnType = paginationMetadata.Structure!.GetColumnSystemType(keyValuePair.Key);
                    if (!ReferenceEquals(value.GetType(), columnType))
                    {
                        throw new ArgumentException($"After param has " +
                            $"incorrect type {value.GetType()} for primary key column {keyValuePair.Key} with type {columnType}.");
                    }

                    after.Add(keyValuePair.Key, valueAndDirection);
                }
            }
            catch (Exception e)
            {
                // Possible sources of exceptions:
                // stringObject cannot be converted to string
                // afterPlainText cannot be successfully decoded
                // afterJsonString cannot be deserialized
                // keys of afterDeserialized do not correspond to the primary key
                // values given for the primary keys are of incorrect format

                if (e is InvalidCastException ||
                    e is ArgumentException ||
                    e is ArgumentNullException ||
                    e is FormatException ||
                    e is System.Text.DecoderFallbackException ||
                    e is JsonException ||
                    e is NotSupportedException
                    )
                {
                    Console.Error.WriteLine(e);
                    string notValidString = $"Parameter after with value {afterJsonString} is not a valid pagination token.";
                    throw new DataGatewayException(notValidString, HttpStatusCode.BadRequest, DataGatewayException.SubStatusCodes.BadRequest);
                }
                else
                {
                    throw;
                }
            }

            return after;
        }

        /// <summary>
        /// Resolves a JsonElement representing a variable to the appropriate type
        /// </summary>
        /// <exception cref="ArgumentException" />
        public static object ResolveJsonElementToScalarVariable(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString()!;
                case JsonValueKind.Number:
                    return element.GetInt64();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    throw new ArgumentException("Unexpected JsonElement value");
            }
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
                    nextLink = @$"{path}?{nvc.ToString()}"
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

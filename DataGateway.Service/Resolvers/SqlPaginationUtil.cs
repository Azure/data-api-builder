using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Contains methods to help generating the *Connection result for pagination
    /// </summary>
    public class SqlPaginationUtil
    {
        /// <summary>
        /// Receives the result of a query and parses:
        /// <list type="bullet">
        /// <list>*Connection.items which is trivially resolved to all the elements of the result (last  discarded if hasNextPage has been requested)</list>
        /// <list>*Connection.endCursur which is the primary key of the last element of the result (last  discarded if hasNextPage has been requested)</list>
        /// <list>*Connection.hasNextPage which is decided on whether structure.Limit() elements have been returned</list>
        /// </list>
        /// </summary>
        public static JsonDocument CreatePaginationConnectionFromDbResult(JsonDocument dbResult, SqlQueryStructure structure)
        {
            // maintains the conneciton JSON object *Connection
            Dictionary<string, object> connectionJson = new();

            // necessary for MsSql because it doesn't coalesce list query results like Postgres
            if (dbResult == null)
            {
                dbResult = JsonDocument.Parse("[]");
            }

            JsonElement root = dbResult.RootElement;
            IEnumerable<JsonElement> rootEnumerated = root.EnumerateArray();

            bool hasExtraElement = false;
            if (structure.IsRequestedPaginationResult("hasNextPage"))
            {
                // check if the number of elements requested is successfully returned
                // structure.Limit() is first + 1 for paginated queries where hasNextPage is requested
                hasExtraElement = rootEnumerated.Count() == structure.Limit();

                // add hasNextPage to connection elements
                connectionJson.Add("hasNextPage", hasExtraElement ? true : false);

                if (hasExtraElement)
                {
                    // remove the last element
                    rootEnumerated = rootEnumerated.Take(rootEnumerated.Count() - 1);
                }
            }

            int returnedElemNo = rootEnumerated.Count();

            if (structure.IsRequestedPaginationResult("items"))
            {
                if (hasExtraElement)
                {
                    // use rootEnumerated to make the *Connection.items since the last element of rootEnumerated
                    // is removed if the result has an extra element
                    connectionJson.Add("items", rootEnumerated.Select(e => e.ToString()).ToArray());
                }
                else
                {
                    // if the result doesn't have an extra element, just return the dbResult for *Conneciton.items
                    connectionJson.Add("items", root.ToString());
                }
            }

            if (structure.IsRequestedPaginationResult("endCursor"))
            {
                // parse *Connection.endCursor if there are no elements
                // if no endCursor is added, but it has been requested HotChocolate will report it as null
                if (returnedElemNo > 0)
                {
                    JsonElement lastElemInRoot = rootEnumerated.ElementAtOrDefault(returnedElemNo - 1);
                    connectionJson.Add("endCursor", MakeCursorFromJsonElement(lastElemInRoot, structure));
                }
            }

            // results have been parsed from the dbResult so the JsonDocument can be disposed
            dbResult.Dispose();

            return JsonDocument.Parse(JsonSerializer.Serialize(connectionJson));
        }

        /// <summary>
        /// Extracts the primary keys from the json elem, puts them in a string in json format and base64 encodes it
        /// The JSON is encoded in base64 for opaqueness. The cursor should function as a token that the user copies and pastes
        /// and doesn't need to know how it works
        /// </summary>
        private static string MakeCursorFromJsonElement(JsonElement element, SqlQueryStructure structure)
        {
            Dictionary<string, object> cursorJson = new();
            List<string> primaryKeys = structure.PrimaryKey();

            foreach (string key in primaryKeys)
            {
                cursorJson.Add(key, structure.ResolveParamTypeFromField(element.GetProperty(key).ToString(), key));
            }

            return Base64Encode(JsonSerializer.Serialize(cursorJson));
        }

        /// <summary>
        /// Parse the value of "after" parameter from query parameters, validate it, and return the json object it stores
        /// </summary>
        public static IDictionary<string, object> ParseAfterFromQueryParams(IDictionary<string, object> queryParams, SqlQueryStructure structure)
        {
            Dictionary<string, object> after = new();
            Dictionary<string, JsonElement> afterDeserialized = new();
            List<string> primaryKeys = structure.PrimaryKey();

            object afterObject = queryParams["after"];
            string afterJsonString;

            if (afterObject != null)
            {
                try
                {
                    string afterPlainText = (string)afterObject;
                    afterJsonString = Base64Decode(afterPlainText);
                    afterDeserialized = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(afterJsonString);

                    if (!ListsAreEqual(afterDeserialized.Keys.ToList(), primaryKeys))
                    {
                        string incorrectValues = $"Parameter \"after\" with values {afterJsonString} does not contain all the required" +
                                                    $"values <{string.Join(", ", primaryKeys.Select(key => $"\"{key}\""))}>";

                        throw new Exception(incorrectValues);
                    }

                    foreach (KeyValuePair<string, JsonElement> keyValuePair in afterDeserialized)
                    {
                        after.Add(keyValuePair.Key, structure.ResolveParamTypeFromField(keyValuePair.Value.ToString(), keyValuePair.Key));
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

                    Console.Error.WriteLine(e);
                    string notValidString = $"Parameter after with value {afterObject} is not a valid base64 encoded json string.";
                    throw new DatagatewayException(notValidString, 400, DatagatewayException.SubStatusCodes.BadRequest);
                }
            }

            return after;
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
        /// Does set-like compare for two string lists.
        /// <summary>
        private static bool ListsAreEqual(List<string> list1, List<string> list2)
        {
            IEnumerable<string> inList1NotInList2 = list1.Except(list2);
            IEnumerable<string> inList2NotInList1 = list2.Except(list1);

            return !inList1NotInList2.Any() && !inList2NotInList1.Any();
        }
    }
}

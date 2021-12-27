using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;
using Newtonsoft.Json;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Contains methods to help generating the *Conntection result for pagination
    /// </summary>
    public class SqlPaginationUtil
    {
        public static JsonDocument CreatePaginationConnectionFromDbResult(JsonDocument dbResult, SqlQueryStructure structure)
        {
            List<string> connectionJsonElems = new();

            if (dbResult == null)
            {
                dbResult = JsonDocument.Parse("[]");
            }

            JsonElement root = dbResult.RootElement;
            IEnumerable<JsonElement> rootElems = root.EnumerateArray();

            bool hasExtraElement = false;
            if (structure.IsRequestedPaginationResult("hasNextPage"))
            {
                hasExtraElement = rootElems.Count() == structure.Limit();
                connectionJsonElems.Add($"\"hasNextPage\": {(hasExtraElement ? "true" : "false")}");

                if (hasExtraElement)
                {
                    // remove the last element
                    rootElems = rootElems.Take(rootElems.Count() - 1);
                }
            }

            int returnedElemNo = rootElems.Count();

            if (structure.IsRequestedPaginationResult("nodes"))
            {
                if (hasExtraElement)
                {
                    connectionJsonElems.Add($"\"nodes\": {'[' + string.Join(", ", rootElems.Select(e => e.GetRawText())) + ']'}");
                }
                else
                {
                    connectionJsonElems.Add($"\"nodes\": {root.GetRawText()}");
                }
            }

            if (structure.IsRequestedPaginationResult("endCursor"))
            {
                if (returnedElemNo > 0)
                {
                    JsonElement lastElemInRoot = rootElems.ElementAtOrDefault(returnedElemNo - 1);
                    connectionJsonElems.Add($"\"endCursor\": {MakeCursorFromJsonElement(lastElemInRoot, structure.PrimaryKey())}");
                }
            }

            return JsonDocument.Parse('{' + string.Join(", ", connectionJsonElems) + '}');
        }

        /// <summary>
        /// Extracts the primary keys from the json elem, puts them in a string in json format and base64 encodes it
        /// </summary>
        private static string MakeCursorFromJsonElement(JsonElement element, List<string> primaryKeys)
        {
            List<string> jsonText = new();

            foreach (string key in primaryKeys)
            {
                jsonText.Add($"\"{key}\": {element.GetProperty(key).GetRawText()}");
            }

            return $"\"{Base64Encode("{ " + string.Join(", ", jsonText) + " }")}\"";
        }

        /// <summary>
        /// Parse the value of "after" parameter from query parameters, validate it, and return the json object it stores
        /// </summary>
        public static IDictionary<string, object> ParseAfterFromQueryParams(IDictionary<string, object> queryParams, List<string> primaryKeys)
        {
            Dictionary<string, object> after = new();

            object afterObject = queryParams["after"];
            string afterJsonString;

            if (afterObject != null)
            {
                try
                {
                    string afterPlainText = (string)afterObject;
                    afterJsonString = Base64Decode(afterPlainText);
                    after = JsonConvert.DeserializeObject<Dictionary<string, object>>(afterJsonString);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                    string notValidString = $"Parameter after with value {afterObject} is not a valid base64 encoded json string.";
                    throw new DatagatewayException(notValidString, 400, DatagatewayException.SubStatusCodes.BadRequest);
                }

                if (!ListsAreEqual(after.Keys.ToList(), primaryKeys))
                {
                    string incorrectValues = $"Parameter \"after\" with values {afterJsonString} does not contain all the required" +
                                                $"values <{string.Join(", ", primaryKeys.Select(key => $"\"{key}\""))}>";

                    throw new DatagatewayException(incorrectValues, 400, DatagatewayException.SubStatusCodes.BadRequest);
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

using Azure.DataGateway.Service.Resolvers;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.DataGateway.Services
{
    public class RequestParser
    {
        /// <summary>
        /// Prefix used for specifying the fields in the query string of the URL.
        /// </summary>
        private const string FIELDS_URL = "_f";

        /// <summary>
        /// Check if the number of query by primaryKey values match the number of column in primary key.
        /// If we did not input the query by primaryKey value, it will also work without considering the query by primaryKey value.
        /// Task 1343542: actual filter by the value of query by primary key.
        /// </summary>
        /// <param name="queryByPrimaryKey">the string contains all the values that will be used when query by primary key.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static void ParsePrimaryKey(string queryByPrimaryKey, FindQueryStructure queryStructure)
        {
            if (!string.IsNullOrWhiteSpace(queryByPrimaryKey))
            {
                string[] primaryKeyValues = queryByPrimaryKey.Split("/");

                if (primaryKeyValues.Length % 2 != 0)
                {
                    throw new NotImplementedException("Support for url template with implicit primary key field names is not yet added.");
                }

                for (int primaryKeyIndex = 0; primaryKeyIndex < primaryKeyValues.Length; primaryKeyIndex += 2)
                {
                    queryStructure.Conditions.Add($"{primaryKeyValues[primaryKeyIndex]} = @{primaryKeyValues[primaryKeyIndex]}");
                    queryStructure.Parameters.Add(primaryKeyValues[primaryKeyIndex], primaryKeyValues[primaryKeyIndex + 1]);
                }

            }
        }

        /// <summary>
        /// ParseQueryString is a helper function used to parse the Query String provided
        /// in the URL of the http request. It parses and saves the values that are needed to
        /// later generate queries, and stores the names of those parameters in _storedParams.
        /// Params from the URL take precedence, _storedParams allows us to later skip params
        /// already loaded from the URL when we parse the body.
        /// </summary>
        /// <param name="nvc">NameValueCollection representing query params from the URL's query string.</param>
        /// <param name="queryStructure">
        public static void ParseQueryString(NameValueCollection nvc, FindQueryStructure queryStructure)
        {
            foreach (string key in nvc.Keys)
            {
                switch (key)
                {
                    case FIELDS_URL:
                        CheckListForNullElement(nvc[key].Split(",").ToList());
                        queryStructure.Fields = nvc[key].Split(",").ToList();
                        break;
                    default:
                        throw new ArgumentException("Invalid Query Parameter: " + key.ToString());
                }
            }
        }

        /// <summary>
        /// CheckListForNullElement is a helper function which checks if any element
        /// in a list meets our definition for null as a column name, and throws an
        /// exception if they do.
        /// </summary>
        /// <param name="list">List of string which represent column names.</param>
        private static void CheckListForNullElement(List<string> list)
        {
            foreach (string word in list)
            {
                if (IsNull(word))
                {
                    throw new ArgumentException("Invalid Column name: null or white space");
                }
            }
        }

        /// <summary>
        /// Helper function checks if string is null or whitespace or contains "null" ignoring caps.
        /// </summary>
        /// <param name="value">String to check for null properties.</param>
        /// <returns>true if null as we have defined it, false otherwise.</returns>
        private static bool IsNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);
        }
    }
}

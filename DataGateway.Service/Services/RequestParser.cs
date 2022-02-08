using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Services
{
    /// <summary>
    /// Class providing parsing logic for different portions of the request url.
    /// </summary>
    public class RequestParser
    {
        /// <summary>
        /// Prefix used for specifying the fields in the query string of the URL.
        /// </summary>
        private const string FIELDS_URL = "_f";
        private const string FILTER_URL = "$filter";
        /// <summary>
        /// Parses the primary key string to identify the field names composing the key
        /// and their values.
        /// </summary>
        /// <param name="primaryKeyRoute">The primary key route. e.g. tablename/{saleOrderId/123/customerName/Xyz/}.</param>
        /// <param name="context">The RestRequestContext holding the major components of the query.</param>
        public static void ParsePrimaryKey(string primaryKeyRoute, RestRequestContext context)
        {
            if (!string.IsNullOrWhiteSpace(primaryKeyRoute))
            {
                string[] primaryKeyValues = primaryKeyRoute.Split("/");

                if (primaryKeyValues.Length % 2 != 0)
                {
                    throw new NotImplementedException("Support for url template with implicit primary key" +
                        " field names is not yet added.");
                }

                for (int primaryKeyIndex = 0; primaryKeyIndex < primaryKeyValues.Length; primaryKeyIndex += 2)
                {
                    string primaryKey = primaryKeyValues[primaryKeyIndex];
                    if (!context.PrimaryKeyValuePairs.ContainsKey(primaryKey))
                    {
                        context.PrimaryKeyValuePairs.Add(primaryKeyValues[primaryKeyIndex],
                            primaryKeyValues[primaryKeyIndex + 1]);
                    }
                    else
                    {
                        throw new DatagatewayException(
                            message: "The request is invalid since it contains duplicate primary keys.",
                            statusCode: 400,
                            DatagatewayException.SubStatusCodes.BadRequest);
                    }
                }
            }
        }

        /// <summary>
        /// ParseQueryString is a helper function used to parse the query String provided
        /// in the URL of the http request. It parses and saves the values that are needed to
        /// later generate queries in the given RestRequestContext.
        /// </summary>
        /// <param name="nvc">NameValueCollection representing query params from the URL's query string.</param>
        /// <param name="context">The RestRequestContext holding the major components of the query.</param>
        public static void ParseQueryString(NameValueCollection nvc, RestRequestContext context, FilterParser filterParser)
        {
            foreach (string key in nvc.Keys)
            {
                switch (key)
                {
                    case FIELDS_URL:
                        CheckListForNullElement(nvc[key].Split(",").ToList());
                        context.FieldsToBeReturned = nvc[key].Split(",").ToList();
                        break;
                    case FILTER_URL:
                        // not yet implemented
                        context.FieldValuePairsInUrl = filterParser.Parse();
                        break;
                    default:
                        throw new ArgumentException("Invalid Query Parameter: " + key.ToString());
                }
            }
        }

        /// <summary>
        /// CheckListForNullElement is a helper function which checks if any element
        /// in the list meets our definition for null as a column name, and throws an
        /// exception if they do.
        /// </summary>
        /// <param name="list">List of string which represents field names.</param>
        private static void CheckListForNullElement(List<string> list)
        {
            foreach (string word in list)
            {
                if (IsNull(word))
                {
                    throw new ArgumentException("Invalid Field name: null or white space");
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

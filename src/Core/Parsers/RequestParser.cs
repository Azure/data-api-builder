// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// Class providing parsing logic for different portions of the request url.
    /// </summary>
    public class RequestParser
    {
        /// <summary>
        /// Prefix used for specifying the fields in the query string of the URL.
        /// </summary>
        public const string FIELDS_URL = "$select";
        /// <summary>
        /// Prefix used for specifying the fields to be used to sort the result in the query string of the URL.
        /// </summary>
        public const string SORT_URL = "$orderby";
        /// <summary>
        /// Prefix used for specifying filter in the query string of the URL.
        /// </summary>
        public const string FILTER_URL = "$filter";
        /// <summary>
        /// Prefix used for specifying limit in the query string of the URL.
        /// </summary>
        public const string FIRST_URL = "$first";
        /// <summary>
        /// Prefix used for specifying paging in the query string of the URL.
        /// </summary>
        public const string AFTER_URL = "$after";

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
                    throw new DataApiBuilderException(
                        message: "Support for url template with implicit primary key field names is not yet added.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                for (int primaryKeyIndex = 0; primaryKeyIndex < primaryKeyValues.Length; primaryKeyIndex += 2)
                {
                    string primaryKey = primaryKeyValues[primaryKeyIndex];

                    if (string.IsNullOrWhiteSpace(primaryKeyValues[primaryKeyIndex + 1]))
                    {
                        throw new DataApiBuilderException(
                            message: "The request is invalid since it contains a primary key with no value specified.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    if (!context.PrimaryKeyValuePairs.ContainsKey(primaryKey))
                    {
                        context.PrimaryKeyValuePairs.Add(primaryKeyValues[primaryKeyIndex],
                            primaryKeyValues[primaryKeyIndex + 1]);
                    }
                    else
                    {
                        throw new DataApiBuilderException(
                            message: "The request is invalid since it contains duplicate primary keys.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                }
            }
        }

        /// <summary>
        /// ParseQueryString is a helper function used to parse the query String provided
        /// in the URL of the http request. It parses and saves the values that are needed to
        /// later generate queries in the given RestRequestContext.
        /// ParsedQueryString is of type NameValueCollection which allows null keys (See documentation),
        /// so any instance of a null key will result in a bad request.
        /// </summary>
        /// <param name="context">The RestRequestContext holding the major components of the query.</param>
        /// <param name="sqlMetadataProvider">The SqlMetadataProvider holds many of the components needed to parse the query.</param>
        /// <seealso cref="https://docs.microsoft.com/dotnet/api/system.collections.specialized.namevaluecollection#remarks"/>
        public static void ParseQueryString(RestRequestContext context, ISqlMetadataProvider sqlMetadataProvider)
        {
            foreach (string key in context.ParsedQueryString!.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new DataApiBuilderException(
                        message: $"A query parameter without a key is not supported.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                switch (key)
                {
                    case FIELDS_URL:
                        CheckListForNullElement(context.ParsedQueryString[key]!.Split(",").ToList());
                        context.FieldsToBeReturned = context.ParsedQueryString[key]!.Split(",").ToList();
                        break;
                    case FILTER_URL:
                        // save the AST that represents the filter for the query
                        // ?$filter=<filter clause using microsoft api guidelines>
                        // Use the raw (URL-encoded) filter value to preserve special characters like &
                        string? rawFilterValue = ExtractRawQueryParameter(context.RawQueryString, FILTER_URL);
                        if (rawFilterValue is not null)
                        {
                            string filterQueryString = $"?{FILTER_URL}={rawFilterValue}";
                            context.FilterClauseInUrl = sqlMetadataProvider.GetODataParser().GetFilterClause(filterQueryString, $"{context.EntityName}.{context.DatabaseObject.FullName}");
                        }
                        break;
                    case SORT_URL:
                        // Use the raw (URL-encoded) orderby value to preserve special characters
                        string? rawSortValue = ExtractRawQueryParameter(context.RawQueryString, SORT_URL);
                        if (rawSortValue is not null)
                        {
                            string sortQueryString = $"?{SORT_URL}={rawSortValue}";
                            (context.OrderByClauseInUrl, context.OrderByClauseOfBackingColumns) = GenerateOrderByLists(context, sqlMetadataProvider, sortQueryString);
                        }
                        break;
                    case AFTER_URL:
                        context.After = context.ParsedQueryString[key];
                        break;
                    case FIRST_URL:
                        context.First = RequestValidator.CheckFirstValidity(context.ParsedQueryString[key]!);
                        break;
                    default:
                        throw new DataApiBuilderException(
                            message: $"Invalid Query Parameter: {key}",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
        }

        /// <summary>
        /// Create List of OrderByColumn from an OrderByClause Abstract Syntax Tree
        /// and return that list as List<Column> since OrderByColumn is a Column.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="sqlMetadataProvider">The meta data provider.</param>
        /// <param name="sortQueryString">String represents the section of the query string
        /// associated with the sort param.</param>
        /// <returns>A List<OrderByColumns></returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public static (List<OrderByColumn>?, List<OrderByColumn>?) GenerateOrderByLists(RestRequestContext context,
                                                                                         ISqlMetadataProvider sqlMetadataProvider,
                                                                                         string sortQueryString)
        {
            string schemaName = context.DatabaseObject.SchemaName;
            string tableName = context.DatabaseObject.Name;

            OrderByClause node = sqlMetadataProvider.GetODataParser().GetOrderByClause(sortQueryString, $"{context.EntityName}.{context.DatabaseObject.FullName}");
            List<string> primaryKeys = sqlMetadataProvider.GetSourceDefinition(context.EntityName).PrimaryKey;

            // used for performant Remove operations
            HashSet<string> remainingKeys = new(primaryKeys);

            List<OrderByColumn> orderByListUrl = new();
            List<OrderByColumn> orderByListBackingColumn = new();

            // OrderBy AST is in the form of a linked list
            // so we traverse by calling node.ThenBy until
            // node is null
            while (node is not null)
            {
                // Column name is stored in node.Expression either as SingleValuePropertyNode, or ConstantNode
                // ConstantNode is used in the case of spaces in column names, and can also be used to support
                // column name of null. ie: $orderby='hello world', or $orderby=null
                // note: null support is not currently implemented.
                QueryNode? expression = node.Expression is not null ? node.Expression :
                                        throw new DataApiBuilderException(
                                            message: "OrderBy property is not supported.",
                                            HttpStatusCode.BadRequest,
                                            DataApiBuilderException.SubStatusCodes.BadRequest);

                string? backingColumnName;
                string exposedName;
                if (expression.Kind is QueryNodeKind.SingleValuePropertyAccess)
                {
                    exposedName = ((SingleValuePropertyAccessNode)expression).Property.Name;
                    sqlMetadataProvider.TryGetBackingColumn(context.EntityName, exposedName, out backingColumnName);
                    // if name is in SingleValuePropertyAccess node it matches our model and we will
                    // always be able to get backing column successfully
                }
                else if (expression.Kind is QueryNodeKind.Constant &&
                        ((ConstantNode)expression).Value is not null)
                {
                    // since this comes from constant node, it was not checked against our model
                    // so this may return false in which case we throw for a bad request
                    exposedName = ((ConstantNode)expression).Value.ToString()!;
                    if (!sqlMetadataProvider.TryGetBackingColumn(context.EntityName, exposedName, out backingColumnName))
                    {
                        throw new DataApiBuilderException(
                            message: $"Invalid orderby column requested: {exposedName}.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                }
                else
                {
                    throw new DataApiBuilderException(
                        message: "OrderBy property is not supported.",
                        HttpStatusCode.BadRequest,
                        DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // Sorting order is stored in node.Direction as OrderByDirection Enum
                // We convert to an Enum of our own that matches the SQL text we want
                OrderBy direction = GetDirection(node.Direction);
                // Add OrderByColumn and remove any matching columns from our primary key set
                orderByListUrl.Add(new OrderByColumn(schemaName, tableName, exposedName, direction: direction));
                orderByListBackingColumn.Add(new OrderByColumn(schemaName, tableName, backingColumnName!, direction: direction));
                remainingKeys.Remove(backingColumnName!);
                node = node.ThenBy;
            }

            // Remaining primary key columns are added here
            // Note that the values of remainingKeys hashset are not printed
            // directly because the hashset does not guarantee order
            foreach (string column in primaryKeys)
            {
                if (remainingKeys.Contains(column))
                {
                    sqlMetadataProvider.TryGetExposedColumnName(context.EntityName, column, out string? exposedName);
                    orderByListUrl.Add(new OrderByColumn(schemaName, tableName, exposedName!));
                    orderByListBackingColumn.Add(new OrderByColumn(schemaName, tableName, column));
                }
            }

            return (orderByListUrl, orderByListBackingColumn);
        }

        /// <summary>
        /// Helper function returns the OrderByDirection associated with a given string
        /// </summary>
        /// <param name="direction">String reprenting the orderby direction.</param>
        /// <returns>Enum representing the direction.</returns>
        private static OrderBy GetDirection(OrderByDirection direction)
        {
            switch (direction)
            {
                case OrderByDirection.Descending:
                    return OrderBy.DESC;
                case OrderByDirection.Ascending:
                    return OrderBy.ASC;
                default:
                    throw new DataApiBuilderException(message: "Invalid order specified in the OrderBy clause.",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
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
                    throw new DataApiBuilderException(message: "Invalid Field name: null or white space",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
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

        /// <summary>
        /// Extracts the raw (URL-encoded) value of a query parameter from a query string.
        /// This preserves special characters like & in filter values.
        /// This method assumes the query string is properly URL-encoded, where parameter
        /// separators are unencoded '&' and special characters within values are encoded (e.g., %26).
        /// </summary>
        /// <param name="queryString">The raw query string (e.g., "?$filter=region%20eq%20%27filter%20%26%20test%27")</param>
        /// <param name="parameterName">The parameter name to extract (e.g., "$filter")</param>
        /// <returns>The raw encoded value of the parameter, or null if not found</returns>
        private static string? ExtractRawQueryParameter(string queryString, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(queryString))
            {
                return null;
            }

            // Remove leading '?' if present
            string query = queryString.TrimStart('?');

            // Split by '&' to get individual parameters
            // This works correctly because in a properly URL-encoded query string:
            // - Parameter separators are unencoded '&'
            // - Special characters within values are encoded (e.g., '&' becomes '%26')
            string[] parameters = query.Split('&');

            foreach (string param in parameters)
            {
                int equalsIndex = param.IndexOf('=');
                if (equalsIndex >= 0)
                {
                    string key = param.Substring(0, equalsIndex);
                    if (string.Equals(key, parameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Return everything after '=', or empty string if '=' is at the end
                        return equalsIndex < param.Length - 1 ? param.Substring(equalsIndex + 1) : string.Empty;
                    }
                }
            }

            return null;
        }
    }
}

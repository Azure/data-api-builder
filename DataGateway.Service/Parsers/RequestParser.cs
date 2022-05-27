using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Microsoft.OData.UriParser;

namespace Azure.DataGateway.Service.Parsers
{
    /// <summary>
    /// Class providing parsing logic for different portions of the request url.
    /// </summary>
    public class RequestParser
    {
        /// <summary>
        /// Prefix used for specifying the fields in the query string of the URL.
        /// </summary>
        private const string FIELDS_URL = "$f";
        /// <summary>
        /// Prefix used for specifying the fields to be used to sort the result in the query string of the URL.
        /// </summary>
        private const string SORT_URL = "$orderby";
        /// <summary>
        /// Prefix used for specifying filter in the query string of the URL.
        /// </summary>
        public const string FILTER_URL = "$filter";
        /// <summary>
        /// Prefix used for specifying limit in the query string of the URL.
        /// </summary>
        private const string FIRST_URL = "$first";
        /// <summary>
        /// Prefix used for specifying paging in the query string of the URL.
        /// </summary>
        private const string AFTER_URL = "$after";
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

                    if (string.IsNullOrWhiteSpace(primaryKeyValues[primaryKeyIndex + 1]))
                    {
                        throw new DataGatewayException(
                            message: "The request is invalid since it contains a primary key with no value specified.",
                            statusCode: HttpStatusCode.BadRequest,
                            DataGatewayException.SubStatusCodes.BadRequest);
                    }

                    if (!context.PrimaryKeyValuePairs.ContainsKey(primaryKey))
                    {
                        context.PrimaryKeyValuePairs.Add(primaryKeyValues[primaryKeyIndex],
                            primaryKeyValues[primaryKeyIndex + 1]);
                    }
                    else
                    {
                        throw new DataGatewayException(
                            message: "The request is invalid since it contains duplicate primary keys.",
                            statusCode: HttpStatusCode.BadRequest,
                            DataGatewayException.SubStatusCodes.BadRequest);
                    }
                }
            }
        }

        /// <summary>
        /// ParseQueryString is a helper function used to parse the query String provided
        /// in the URL of the http request. It parses and saves the values that are needed to
        /// later generate queries in the given RestRequestContext.
        /// </summary>
        /// <param name="context">The RestRequestContext holding the major components of the query.</param>
        public static void ParseQueryString(RestRequestContext context, FilterParser filterParser, List<string> primaryKeys)
        {
            foreach (string key in context.ParsedQueryString!.Keys)
            {
                switch (key)
                {
                    case FIELDS_URL:
                        CheckListForNullElement(context.ParsedQueryString[key]!.Split(",").ToList());
                        context.FieldsToBeReturned = context.ParsedQueryString[key]!.Split(",").ToList();
                        break;
                    case FILTER_URL:
                        // save the AST that represents the filter for the query
                        // ?$filter=<filter clause using microsoft api guidelines>
                        string filterQueryString = $"?{FILTER_URL}={context.ParsedQueryString[key]}";
                        context.FilterClauseInUrl = filterParser.GetFilterClause(filterQueryString, $"{context.DatabaseObject.FullName}");
                        break;
                    case SORT_URL:
                        string sortQueryString = $"?{SORT_URL}={context.ParsedQueryString[key]}";
                        context.OrderByClauseInUrl = GenerateOrderByList(filterParser.GetOrderByClause(sortQueryString, $"{context.DatabaseObject.FullName}"),
                                                                         context.DatabaseObject.SchemaName,
                                                                         context.DatabaseObject.Name,
                                                                         primaryKeys);
                        // to allow spaces in columns we accept constant value node
                        // which means the model no longer validates all columns
                        // automatically, so we must do so explicitly
                        RequestValidator.CheckOrderByValidity(context);
                        break;
                    case AFTER_URL:
                        context.After = context.ParsedQueryString[key];
                        break;
                    case FIRST_URL:
                        context.First = RequestValidator.CheckFirstValidity(context.ParsedQueryString[key]!);
                        break;
                    default:
                        throw new ArgumentException($"Invalid Query Parameter: {key.ToString()}");
                }
            }
        }

        /// <summary>
        /// Create List of OrderByColumn from an OrderByClause Abstract Syntax Tree
        /// and return that list as List<Column> since OrderByColumn is a Column.
        /// </summary>
        /// <param name="node">The OrderByClause.</param>
        /// <param name="tableAlias">The name of the Table the columns are from.</param>
        /// <paramref name="primaryKeys">A list of the primaryKeys of the given table.</paramref>/>
        /// <returns>A List<OrderByColumns></returns>
        private static List<OrderByColumn>? GenerateOrderByList(OrderByClause node, string schemaName, string tableName, List<string> primaryKeys)
        {
            // used for performant Remove operations
            HashSet<string> remainingKeys = new(primaryKeys);

            List<OrderByColumn> orderByList = new();
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
                                        throw new DataGatewayException(message: "OrderBy property is not supported.",
                                                                       HttpStatusCode.BadRequest,
                                                                       DataGatewayException.SubStatusCodes.BadRequest);

                string columnName;
                if (expression.Kind is QueryNodeKind.SingleValuePropertyAccess)
                {
                    // assignment of columnName will need to change when mapping work item merges
                    // see: https://github.com/Azure/hawaii-gql/pull/421
                    columnName = ((SingleValuePropertyAccessNode)expression).Property.Name;
                }
                else if (expression.Kind is QueryNodeKind.Constant &&
                        ((ConstantNode)expression).Value is not null)
                {
                    // assignment of columnName will need to change when mapping work item merges
                    // see: https://github.com/Azure/hawaii-gql/pull/421
                    columnName = ((ConstantNode)expression).Value.ToString()!;
                }
                else
                {
                    throw new DataGatewayException(message: "OrderBy property is not supported.",
                                                   HttpStatusCode.BadRequest,
                                                   DataGatewayException.SubStatusCodes.BadRequest);
                }

                // Sorting order is stored in node.Direction as OrderByDirection Enum
                // We convert to an Enum of our own that matches the SQL text we want
                Models.OrderByDir direction = GetDirection(node.Direction);
                // Add OrderByColumn and remove any matching columns from our primary key set
                orderByList.Add(new OrderByColumn(schemaName, tableName, columnName, direction: direction));
                remainingKeys.Remove(columnName);
                node = node.ThenBy;
            }

            // Remaining primary key columns are added here
            // Note that the values of remainingKeys hashset are not printed
            // directly because the hashset does not guarantee order
            foreach (string column in primaryKeys)
            {
                if (remainingKeys.Contains(column))
                {
                    orderByList.Add(new OrderByColumn(schemaName, tableName, column));
                }
            }

            return orderByList;
        }

        /// <summary>
        /// Helper function returns the OrderByDirection associated with a given string
        /// </summary>
        /// <param name="direction">String reprenting the orderby direction.</param>
        /// <returns>Enum representing the direction.</returns>
        private static Models.OrderByDir GetDirection(OrderByDirection direction)
        {
            switch (direction)
            {
                case OrderByDirection.Descending:
                    return Models.OrderByDir.Desc;
                case OrderByDirection.Ascending:
                    return Models.OrderByDir.Asc;
                default:
                    throw new DataGatewayException(message: "Invalid order specified in the OrderBy clause.",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Service.Parsers
{
    /// <summary>
    /// Class providing parsing logic for different portions of the request url.
    /// </summary>
    public class RequestParser
    {
        /// <summary>
        /// Prefix used for specifying the fields in the query string of the URL.
        /// </summary>
        private const string FIELDS_URL = "$select";
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
        /// </summary>
        /// <param name="context">The RestRequestContext holding the major components of the query.</param>
        /// <param name="sqlMetadataProvider">The SqlMetadataProvider holds many of the components needed to parse the query.</param>
        public static void ParseQueryString(RestRequestContext context, ISqlMetadataProvider sqlMetadataProvider)
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
                        context.FilterClauseInUrl = sqlMetadataProvider.GetODataParser().GetFilterClause(filterQueryString, $"{context.EntityName}.{context.DatabaseObject.FullName}");
                        break;
                    case SORT_URL:
                        string sortQueryString = $"?{SORT_URL}={context.ParsedQueryString[key]}";
                        context.OrderByClauseInUrl = GenerateOrderByList(context, sqlMetadataProvider, sortQueryString);
                        break;
                    case AFTER_URL:
                        context.After = context.ParsedQueryString[key];
                        break;
                    case FIRST_URL:
                        context.First = RequestValidator.CheckFirstValidity(context.ParsedQueryString[key]!);
                        break;
                    default:
                        throw new DataApiBuilderException(message: $"Invalid Query Parameter: {key.ToString()}",
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
        private static List<OrderByColumn>? GenerateOrderByList(RestRequestContext context,
                                                                ISqlMetadataProvider sqlMetadataProvider,
                                                                string sortQueryString)
        {
            string schemaName = context.DatabaseObject.SchemaName;
            string tableName = context.DatabaseObject.Name;

            OrderByClause node = sqlMetadataProvider.GetODataParser().GetOrderByClause(sortQueryString, $"{context.EntityName}.{context.DatabaseObject.FullName}");
            List<string> primaryKeys = sqlMetadataProvider.GetTableDefinition(context.EntityName).PrimaryKey;

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
                                        throw new DataApiBuilderException(message: "OrderBy property is not supported.",
                                                                       HttpStatusCode.BadRequest,
                                                                       DataApiBuilderException.SubStatusCodes.BadRequest);

                string backingColumnName;
                if (expression.Kind is QueryNodeKind.SingleValuePropertyAccess)
                {
                    // if name is in SingleValuePropertyAccess node it matches our model and we will
                    // always be able to get backing column successfully
                    sqlMetadataProvider.TryGetBackingColumn(context.EntityName, ((SingleValuePropertyAccessNode)expression).Property.Name, out backingColumnName!);
                }
                else if (expression.Kind is QueryNodeKind.Constant &&
                        ((ConstantNode)expression).Value is not null)
                {
                    // since this comes from constant node, it was not checked against our model
                    // so this may return false in which case we throw for a bad request
                    if (!sqlMetadataProvider.TryGetBackingColumn(context.EntityName, ((ConstantNode)expression).Value.ToString()!, out backingColumnName!))
                    {
                        throw new DataApiBuilderException(
                            message: $"Invalid orderby column requested: {((ConstantNode)expression).Value.ToString()!}.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                }
                else
                {
                    throw new DataApiBuilderException(message: "OrderBy property is not supported.",
                                                   HttpStatusCode.BadRequest,
                                                   DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // Sorting order is stored in node.Direction as OrderByDirection Enum
                // We convert to an Enum of our own that matches the SQL text we want
                OrderBy direction = GetDirection(node.Direction);
                // Add OrderByColumn and remove any matching columns from our primary key set
                orderByList.Add(new OrderByColumn(schemaName, tableName, backingColumnName, direction: direction));
                remainingKeys.Remove(backingColumnName);
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
    }
}

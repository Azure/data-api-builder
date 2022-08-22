using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Parsers;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// RestRequestContext defining the properties that each REST API request operations have
    /// in common.
    /// </summary>
    public abstract class RestRequestContext
    {
        protected RestRequestContext(string entityName, DatabaseObject dbo)
        {
            EntityName = entityName;
            DatabaseObject = dbo;
        }

        /// <summary>
        /// The target Entity on which the request needs to be operated upon.
        /// </summary>
        public string EntityName { get; }

        /// <summary>
        /// The database object associated with the target entity.
        /// </summary>
        public DatabaseObject DatabaseObject { get; }

        /// <summary>
        /// Field names of the entity that are queried in the request.
        /// </summary>
        public List<string> FieldsToBeReturned { get; set; } = new();

        /// <summary>
        /// Dictionary of primary key and their values specified in the request.
        /// When there are multiple values, that means its a composite primary key.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual Dictionary<string, object> PrimaryKeyValuePairs { get; set; } = new();

        /// <summary>
        /// AST that represents the filter part of the query.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual FilterClause? FilterClauseInUrl { get; set; }

        /// <summary>
        /// AST that represents the filter part added to the query because of database policy.
        /// </summary>
        public virtual FilterClause? DbPolicyClause { get; set; }

        /// <summary>
        /// List of OrderBy Columns which represent the OrderByClause from the URL.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual List<OrderByColumn>? OrderByClauseInUrl { get; set; }

        /// <summary>
        /// Dictionary of field names and their values given in the request body.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual Dictionary<string, object?> FieldValuePairsInBody { get; set; } = new();

        /// <summary>
        /// NVC stores the query string parsed into a NameValueCollection.
        /// </summary>
        public NameValueCollection ParsedQueryString { get; set; } = new();

        /// <summary>
        /// String holds information needed for pagination.
        /// Based on request this property may or may not be populated.
        /// </summary>
        public string? After { get; set; }

        /// <summary>
        /// uint holds the number of records to retrieve.
        /// Based on request this property may or may not be populated.
        /// </summary>

        public uint? First { get; set; }
        /// <summary>
        /// Is the result supposed to be multiple values or not.
        /// </summary>

        public bool IsMany { get; set; }

        /// <summary>
        /// The database engine operation type this request is.
        /// </summary>
        public Operation OperationType { get; set; }

        /// <summary>
        /// A collection of all unique column names present in the request.
        /// </summary>
        public ISet<string> CumulativeColumns { get; } = new HashSet<string>();

        /// <summary>
        /// Populates the CumulativeColumns property with a unique list
        /// of all columns present in a request. Primarily used
        /// for authorization purposes.
        /// # URL Route Components: PrimaryKey Key/Value Pairs
        /// # Query String components: $f (Column filter), $filter (FilterClause /row filter), $orderby clause
        /// # Request Body: FieldValuePairs in body
        /// </summary>
        /// <returns>
        /// Returns true on success, false on failure.
        /// </returns>
        public void CalculateCumulativeColumns()
        {
            try
            {
                if (PrimaryKeyValuePairs.Count > 0)
                {
                    CumulativeColumns.UnionWith(PrimaryKeyValuePairs.Keys);
                }

                if (FieldsToBeReturned.Count > 0)
                {
                    CumulativeColumns.UnionWith(FieldsToBeReturned);
                }

                if (FilterClauseInUrl is not null)
                {
                    ODataASTFieldVisitor visitor = new();
                    FilterClauseInUrl.Expression.Accept(visitor);
                    CumulativeColumns.UnionWith(visitor.CumulativeColumns);
                }

                if (OrderByClauseInUrl is not null)
                {
                    CumulativeColumns.UnionWith(OrderByClauseInUrl.Select(col => col.ColumnName));
                }

                if (FieldValuePairsInBody.Count > 0)
                {
                    CumulativeColumns.UnionWith(FieldValuePairsInBody.Keys);
                }
            }
            catch (Exception e)
            {
                // Exception not rethrown as returning false here is gracefully handled by caller,
                // which will result in a 403 Unauthorized response to the client.
                Console.Error.WriteLine("ERROR IN ODATA_AST_COLUMN_VISITOR TRAVERSAL");
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(e.StackTrace);
                throw new DataApiBuilderException(
                    message: "Request content invalid.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCumulativeColumnCheckFailed);
            }
        }

        /// <summary>
        /// Modifies the contents of FieldsToBeReturned.
        /// This method is only called when FieldsToBeReturned is empty.
        /// </summary>
        /// <param name="fields">Collection of fields to be returned.</param>
        public void UpdateReturnFields(IEnumerable<string> fields)
        {
            FieldsToBeReturned = fields.ToList();
        }

        /// <summary>
        /// Tries to parse the json request body into FieldValuePairsInBody dictionary
        /// </summary>
        public void PopulateFieldValuePairsInBody(JsonElement? jsonBody)
        {
            string? payload = jsonBody.ToString();
            if (!string.IsNullOrEmpty(payload))
            {
                try
                {
                    Dictionary<string, object?>? fieldValuePairs = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload);
                    if (fieldValuePairs is not null)
                    {
                        FieldValuePairsInBody = fieldValuePairs;
                    }
                    else
                    {
                        throw new JsonException("Failed to deserialize the request body payload");
                    }
                }
                catch (JsonException)
                {
                    throw new DataApiBuilderException(
                        message: "The request body is not in a valid JSON format.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
            else
            {
                FieldValuePairsInBody = new();
            }
        }
    }
}

using System.Collections.Generic;
using System.Collections.Specialized;
using Azure.DataGateway.Config;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.OData.UriParser;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// RestRequestContext defining the properties that each REST API request operations have
    /// in common.
    /// </summary>
    public abstract class RestRequestContext
    {
        protected RestRequestContext(OperationAuthorizationRequirement httpVerb, string entityName)
        {
            HttpVerb = httpVerb;
            EntityName = entityName;
        }

        /// <summary>
        /// The target Entity on which the request needs to be operated upon.
        /// </summary>
        public string EntityName { get; set; }

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
        public NameValueCollection? ParsedQueryString { get; set; } = new();

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
        /// The REST verb this request is.
        /// </summary>
        public OperationAuthorizationRequirement HttpVerb { get; init; }

        /// <summary>
        /// The database engine operation type this request is.
        /// </summary>
        public Operation OperationType { get; set; }
    }
}

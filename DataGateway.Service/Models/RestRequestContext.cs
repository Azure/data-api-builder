using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// RestRequestContext defining the properties that each REST API request operations have
    /// in common.
    /// </summary>
    public abstract class RestRequestContext
    {
        /// <summary>
        /// The target Entity on which the request needs to be operated upon.
        /// </summary>
        public string EntityName { get; set; }

        /// <summary>
        /// Field names of the entity that are queried in the request.
        /// </summary>
        public List<string> FieldsToBeReturned { get; set; }

        /// <summary>
        /// Dictionary of primary key and their values specified in the request.
        /// When there are multiple values, that means its a composite primary key.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual Dictionary<string, object> PrimaryKeyValuePairs { get; set; }

        /// <summary>
        /// Dictionary of field names and a tuple which holds the associated value and
        /// predicate operation. Where value is the value compared to the field and
        /// the predicate operation is the sort of comparison done.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public Dictionary<string, Tuple<object, PredicateOperation>> FieldValuePairsInUrl { get; set; }

        /// <summary>
        /// Dictionary of field names and their values given in the request body.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual Dictionary<string, object> FieldValuePairsInBody { get; set; }

        /// <summary>
        /// Is the result supposed to be multiple values or not.
        /// </summary>
        public bool IsMany { get; set; }

        /// <summary>
        /// The REST verb this request is.
        /// </summary>
        public OperationAuthorizationRequirement HttpVerb { get; set; }

        /// <summary>
        /// The database engine operation type this request is.
        /// </summary>
        public Operation OperationType { get; set; }
    }
}

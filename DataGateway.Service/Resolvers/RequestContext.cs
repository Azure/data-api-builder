using System.Collections.Generic;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// RequestContext defining the properties that each REST API request operations have
    /// in common.
    /// </summary>
    public abstract class RequestContext
    {
        /// <summary>
        /// The target Entity on which the request needs to be operated upon.
        /// </summary>
        public string EntityName { get; set; }

        /// <summary>
        /// Field names of the entity that are queried in the request.
        /// </summary>
        public List<string> Fields { get; set; }

        /// <summary>
        /// Dictionary of field names and their values given in the request.
        /// </summary>
        public Dictionary<string, object> FieldValuePairs { get; set; }

        /// <summary>
        /// Is the result supposed to be multiple values or not.
        /// </summary>
        public bool IsMany { get; set; }

        /// <summary>
        /// The type of operation this request is.
        /// </summary>
        public Operation OperationType { get; set; }
    }
}

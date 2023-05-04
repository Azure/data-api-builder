// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.DatabasePrimitives;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// DeleteRequestContext provides the major components of a REST query
    /// corresponding to the DeleteById or DeleteMany operations.
    /// </summary>
    public class DeleteRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public DeleteRequestContext(string entityName, DatabaseObject dbo, bool isList)
            : base(entityName, dbo)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            FieldValuePairsInBody = new();
            IsMany = isList;
            OperationType = Config.EntityActionOperation.Delete;
        }
    }
}

// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: UpsertRequestContext.cs
// **************************************

using System.Text.Json;
using Azure.DataApiBuilder.Config;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// UpsertRequestContext provides the major components of a REST query
    /// corresponding to the PutOne operations.
    /// </summary>
    public class UpsertRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public UpsertRequestContext(
            string entityName,
            DatabaseObject dbo,
            JsonElement insertPayloadRoot,
            Config.Operation operationType)
            : base(entityName, dbo)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            OperationType = operationType;

            PopulateFieldValuePairsInBody(insertPayloadRoot);

            // We don't support UpsertMany as yet.
            IsMany = false;
        }
    }
}


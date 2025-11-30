// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// InsertRequestContext provides the major components of a REST query
/// corresponding to the InsertOne or InsertMany operations.
/// </summary>
public class InsertRequestContext : RestRequestContext
{
    /// <summary>
    /// Constructor.
    /// </summary>
    public InsertRequestContext(
        string entityName,
        DatabaseObject dbo,
        JsonElement insertPayloadRoot,
        EntityActionOperation HttpMethod)
        : base(entityName, dbo)
    {
        this.HttpMethod = HttpMethod;
        FieldsToBeReturned = new();
        PrimaryKeyValuePairs = new();

        PopulateFieldValuePairsInBody(insertPayloadRoot);
        // We don't support InsertMany as yet.
        IsMany = false;
    }
}

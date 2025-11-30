// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Models;

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
        EntityActionOperation HttpMethod)
        : base(entityName, dbo)
    {
        this.HttpMethod = HttpMethod;
        FieldsToBeReturned = new();
        PrimaryKeyValuePairs = new();

        PopulateFieldValuePairsInBody(insertPayloadRoot);

        // We don't support UpsertMany as yet.
        IsMany = false;
    }
}


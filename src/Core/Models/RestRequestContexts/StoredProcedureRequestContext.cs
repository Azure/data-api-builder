// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// StoredProcedureRequestContext provides all needed request context for a stored procedure query.
/// For Find requests, parameters will be passed in the query string, which we can access from the base class's
/// ParsedQueryString field; for all other Operation types, we can populate and use the FieldValuePairsInBody
/// </summary>
public class StoredProcedureRequestContext : RestRequestContext
{
    /// <summary>
    /// Represents the parameters that this request is calling the stored procedure with
    /// </summary>
    public Dictionary<string, object?> ResolvedParameters { get; private set; } = new();

    /// <summary>
    /// Represents a request to execute a stored procedure. At the time of construction, populates the FieldValuePairsInBody
    /// </summary>
    public StoredProcedureRequestContext(
        string entityName,
        DatabaseObject dbo,
        JsonElement? requestPayloadRoot,
        EntityActionOperation operationType)
        : base(entityName, dbo)
    {
        FieldsToBeReturned = new();
        OperationType = operationType;

        PopulateFieldValuePairsInBody(requestPayloadRoot);
    }

    /// <summary>
    /// Resolves the parameters that will be passed to the SqlExecuteQueryStructure constructor
    /// This method should be called after the FieldValuePairsInBody and ParsedQueryString collections are filled
    /// For Find operation, parameters are resolved using the query string; for all others, the request body
    /// </summary>
    public void PopulateResolvedParameters()
    {
        if (OperationType is EntityActionOperation.Read)
        {
            // Query string may have malformed/null keys, if so just ignore them
            ResolvedParameters = ParsedQueryString.Cast<string>()
                .Where(k => k is not null).ToDictionary(k => k, k => (object?)ParsedQueryString[k]);
        }
        else
        {
            ResolvedParameters = FieldValuePairsInBody;
        }
    }

}

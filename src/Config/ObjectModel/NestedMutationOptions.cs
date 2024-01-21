// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Class that holds the options for all nested mutation operations.
/// </summary>
/// <param name="NestedInsertOptions"></param>
public class NestedMutationOptions
{
    // Options for nested insert operation.
    [JsonPropertyName("insert")]
    public NestedInsertOptions? NestedInsertOptions;

    public NestedMutationOptions(NestedInsertOptions? nestedInsertOptions)
    {
        NestedInsertOptions = nestedInsertOptions;
    }

    /// <summary>
    /// Helper function that checks if nested insert operation is enabled.
    /// </summary>
    /// <returns>True/False depending on whether nested insert operation is enabled/disabled.</returns>
    public bool IsNestedInsertOperationEnabled()
    {
        return NestedInsertOptions is not null && NestedInsertOptions.Enabled;
    }

}

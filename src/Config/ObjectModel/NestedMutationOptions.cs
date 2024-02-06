// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Class that holds the options for all nested mutation operations.
/// </summary>
/// <param name="NestedCreateOptions">Options for nested create operation.</param>
public class NestedMutationOptions
{
    // Options for nested create operation.
    [JsonPropertyName("insert")]
    public NestedCreateOptions? NestedCreateOptions;

    public NestedMutationOptions(NestedCreateOptions? nestedCreateOptions = null)
    {
        NestedCreateOptions = nestedCreateOptions;
    }

    /// <summary>
    /// Helper function that checks if nested create operation is enabled.
    /// </summary>
    /// <returns>True/False depending on whether nested create operation is enabled/disabled.</returns>
    public bool IsNestedCreateOperationEnabled()
    {
        return NestedCreateOptions is not null && NestedCreateOptions.Enabled;
    }

}

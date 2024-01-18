// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// 
/// </summary>
/// <param name="NestedInsertOptions"></param>
public class NestedMutationOptions
{
    [JsonPropertyName("insert")]
    public NestedInsertOptions? NestedInsertOptions;

    public NestedMutationOptions(NestedInsertOptions? nestedInsertOptions)
    {
        NestedInsertOptions = nestedInsertOptions;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public bool IsNestedInsertOperationEnabled()
    {
        return NestedInsertOptions is not null && NestedInsertOptions.Enabled;
    }

}

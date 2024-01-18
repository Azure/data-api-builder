// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// 
/// </summary>
/// <param name="Enabled"></param>
public class NestedInsertOptions
{
    public bool Enabled;

    public NestedInsertOptions(bool enabled)
    {
        Enabled = enabled;
    }
};

